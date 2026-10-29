using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AcademicDocx.Core;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

[Collection("Word")]
public sealed class QuartoStarterTests
{
    [WordFact]
    public void IndependentProjectsUseProfilesAndFindTheServiceOnPath()
    {
        var source = TemplateSource();
        var artifacts = Environment.GetEnvironmentVariable("ACADEMIC_TEST_ARTIFACTS");
        var root = Path.GetFullPath(artifacts ?? Path.GetTempPath());
        var parent = Path.Combine(root, "ANK starter проверка " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        foreach (var profile in new[] { "coursework", "referat" })
        {
            var project = Path.Combine(parent, profile);
            Directory.CreateDirectory(project);
            MustRun(project, "quarto", ["use", "template", source, "--no-prompt"]);
            Assert.True(File.Exists(Path.Combine(project, ".Rprofile")));
            Assert.True(File.Exists(Path.Combine(project, ".gitignore")));
            Assert.True(File.Exists(Path.Combine(project, "AGENTS.md")));
            Assert.True(File.Exists(Path.Combine(project, "EXAMPLE_GUIDE.md")));
            Assert.True(File.Exists(Path.Combine(project, "data", ".gitkeep")));
            Assert.False(Directory.Exists(Path.Combine(project, "renv", "library")));
            Assert.False(Directory.Exists(Path.Combine(project, "docs")));
            Assert.False(Directory.Exists(Path.Combine(project, "tools", "AcademicDocx")));
            // Exercise the namespace added by a GitHub installation, too.
            if (profile == "referat")
            {
                Directory.CreateDirectory(Path.Combine(project, "_extensions", "test-owner"));
                Directory.Move(Path.Combine(project, "_extensions", "academic"),
                    Path.Combine(project, "_extensions", "test-owner", "academic"));
            }
            var metadata = Path.Combine(project, "metadata.yml");
            var yaml = Regex.Replace(File.ReadAllText(metadata), "(?m)^title:.*$",
                "title: Независимая работа " + profile).Replace("profile: coursework", "profile: " + profile);
            if (profile == "referat")
                yaml = string.Join('\n', yaml.Split('\n').Where(line => !line.TrimStart().StartsWith("summary:", StringComparison.Ordinal)));
            File.WriteAllText(metadata, yaml);
            var main = Path.Combine(project, "chapters", "main.qmd");
            if (profile == "referat")
                File.WriteAllText(main, File.ReadAllText(main).Replace("a = 3,", "a = 5,"));
            MustRun(project, "Rscript", ["--vanilla", "tools/install.R"]);
            MustRun(project, "quarto", ["render"]);
            var output = Directory.GetFiles(Path.Combine(project, "_output"), "*.docx", SearchOption.AllDirectories).Single();
            Assert.Equal(0, DocxInspection.Validate(output).ErrorCount);
            var facts = DocxInspection.Read(output);
            Assert.True(facts.MathCount > 0);
            Assert.Contains(facts.Tables, table => table.MathCount > 0);
            Assert.Contains(facts.Fields, field => field.StartsWith("REF eq_model", StringComparison.Ordinal));
            Assert.Contains(facts.Fields, field => field.StartsWith("SEQ ТаблицаAppendixB", StringComparison.Ordinal));
            using var package = WordprocessingDocument.Open(output, false);
            var mainPart = package.MainDocumentPart!;
            // Word may deduplicate direct sizes into paragraph/table styles;
            // effective text and OMML sizes are checked through the Word API.
            var styles = mainPart.StyleDefinitionsPart!.Styles!;
            foreach (var link in styles.Elements<W.Style>().Where(s =>
                s.StyleName?.Val?.Value is "Hyperlink" or "FollowedHyperlink"))
                Assert.Equal("000000", link.StyleRunProperties!.GetFirstChild<W.Color>()!.Val!.Value);
            foreach (var caption in mainPart.Document.Descendants<W.Paragraph>().Where(p =>
                p.Descendants<W.FieldCode>().Any(f => f.Text.TrimStart().StartsWith("SEQ Рисунок", StringComparison.Ordinal))))
                Assert.DoesNotContain(caption.Elements<W.ParagraphProperties>(), properties =>
                    properties.Justification?.Val?.Value == W.JustificationValues.Left);
            var text = string.Join(" ", mainPart.Document.Descendants<W.Text>().Select(t => t.Text));
            Assert.Contains("Независимая работа " + profile, text);
            Assert.Contains("Титульный лист — ЗАГЛУШКА", text);
            Assert.Contains("ПРИЛОЖЕНИЕ А", text);
            Assert.Contains("ПРИЛОЖЕНИЕ Б", text);
            Assert.Contains("Полный исходный код вычисления показателей", text);
            Assert.Contains(mainPart.ImageParts, part => part.ContentType == "image/svg+xml");
            Assert.True(mainPart.ImageParts.Count(part => part.ContentType == "image/svg+xml") >= 4);
            Assert.NotNull(mainPart.FootnotesPart);
            Assert.Contains(mainPart.FootnotesPart!.Footnotes!.Elements<W.Footnote>(), n => (n.Id?.Value ?? 0) > 0);
            Assert.DoesNotContain("??", text);
            Assert.DoesNotMatch(@"\(\s*\(\s*\d", text);
            Assert.Single(Regex.Matches(text, "Расширенная таблица и её итог"));
            Assert.Matches(@"Суммарный показатель:\s*" + (profile == "coursework" ? "108" : "166"), text);
            Assert.True(File.Exists(Path.Combine(project, "_output", "assets", "functions-600dpi.png")));
            Assert.True(File.Exists(Path.Combine(project, "_output", "assets", "results.csv")));
            if (profile == "coursework")
            {
                Assert.Contains("Задание — ЗАГЛУШКА", text);
                Assert.Contains("Аннотация — ЗАГЛУШКА", text);
                // Fail before DOCX generation instead of silently blanking a sheet.
                File.WriteAllText(metadata, string.Join('\n', yaml.Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("summary:", StringComparison.Ordinal))));
                var missing = Run(project, "quarto", ["render"]);
                Assert.NotEqual(0, missing.Code);
                Assert.Contains("academic.summary", missing.Output);
            }
            else
            {
                Assert.DoesNotContain("Задание — ЗАГЛУШКА", text);
                Assert.DoesNotContain("Аннотация — ЗАГЛУШКА", text);
                File.WriteAllText(metadata, yaml.Replace("profile: referat", "profile: missing-profile"));
                var unknown = Run(project, "quarto", ["render"]);
                Assert.NotEqual(0, unknown.Code);
                Assert.Contains("Unknown or missing academic.profile", unknown.Output);
            }
        }
        if (artifacts is not null)
            File.WriteAllText(Path.Combine(parent, "source.txt"), source);
        else
        {
            // Delete only this successful run's own directory. Failed runs remain
            // available for diagnostics; an explicit artifacts directory is kept.
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(parent).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup target escapes its temporary root");
            Directory.Delete(parent, recursive: true);
        }
    }

    private static string TemplateSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, "quarto");
            if (File.Exists(Path.Combine(source, "_extensions", "academic", "_extension.yml"))) return source;
        }
        throw new InvalidOperationException("Run the starter tests from an ANK DOCX source checkout");
    }

    private static void MustRun(string directory, string command, string[] args)
    {
        var result = Run(directory, command, args);
        Assert.True(result.Code == 0, result.Output);
    }

    private static (int Code, string Output) Run(string directory, string command, string[] args)
    {
        var info = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var service = Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE")
            ?? throw new InvalidOperationException("Published service path is required for the tests");
        info.Environment["PATH"] = Path.GetDirectoryName(service) + ";" + info.Environment["PATH"];
        info.Environment.Remove("ACADEMIC_DOCX_SERVICE");
        info.Environment.Remove("ACADEMIC_SKIP_WORD");
        foreach (var key in new[] { "LC_ALL", "LC_CTYPE", "LC_COLLATE", "LC_MONETARY", "LC_TIME" }) info.Environment.Remove(key);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException(command + " timed out"); }
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        File.AppendAllText(Path.Combine(directory, "starter-test.log"), command + " " + string.Join(' ', args) + "\n" + output + "\n");
        return (process.ExitCode, output);
    }
}
