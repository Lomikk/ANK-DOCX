using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
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
            Assert.False(File.Exists(Path.Combine(project, "EXAMPLE_GUIDE.md")));
            Assert.False(Directory.Exists(Path.Combine(project, "examples")));
            Assert.Equal("", File.ReadAllText(Path.Combine(project, "references.bib")));
            Assert.DoesNotContain("calculate-model", File.ReadAllText(Path.Combine(project, "chapters", "main.qmd")));
            Assert.True(File.Exists(Path.Combine(project, "data", ".gitkeep")));
            Assert.False(Directory.Exists(Path.Combine(project, "renv", "library")));
            Assert.False(Directory.Exists(Path.Combine(project, "docs")));
            Assert.False(Directory.Exists(Path.Combine(project, "tools", "AcademicDocx")));
            var metadata = Path.Combine(project, "metadata.yml");
            // Fill authored metadata and text explicitly from the repository's
            // reference example, after asserting that the new work is clean.
            var reference = Path.GetFullPath(Path.Combine(source, "..", "Workspaces", "Пример"));
            var yaml = Regex.Replace(File.ReadAllText(Path.Combine(reference, "metadata.yml")), "(?m)^title:.*$",
                "title: Независимая работа " + profile);
            // Retain one legacy profile path while the technical test uses a full ID.
            yaml = Regex.Replace(yaml, "(?m)^  profile:.*$", "  profile: " + profile);
            if (profile == "referat")
                yaml = yaml.Replace("Название университета", "Федеральное государственное автономное образовательное учреждение высшего образования «Испытательный университет научных исследований и информационных технологий»");
            File.WriteAllText(metadata, yaml);
            var main = Path.Combine(project, "chapters", "main.qmd");
            File.Copy(Path.Combine(reference, "chapters", "main.qmd"), main, overwrite: true);
            File.Copy(Path.Combine(reference, "references.bib"), Path.Combine(project, "references.bib"), overwrite: true);
            File.AppendAllText(Path.Combine(project, "references.bib"), "\n@book{unused-source, title={Unused source}, author={Example, A}, year={2020}}\n");
            if (profile == "referat")
                File.WriteAllText(main, File.ReadAllText(main).Replace("a = 3,", "a = 5,"));
            MustRun(project, "Rscript", ["--vanilla", "tools/install.R"]);
            var packages = Directory.GetDirectories(Path.Combine(project, "renv", "library"), "jsonlite", SearchOption.AllDirectories);
            Assert.Single(packages);
            Assert.True(new DirectoryInfo(packages[0]).Attributes.HasFlag(FileAttributes.ReparsePoint), "R packages should link to the shared cache");
            var authored = File.ReadAllText(main);
            var metadataBeforeUpdate = File.ReadAllText(metadata);
            MustRun(project, "quarto", ["add", source, "--no-prompt"]);
            Assert.Equal(authored, File.ReadAllText(main));
            Assert.Equal(metadataBeforeUpdate, File.ReadAllText(metadata));
            // Exercise the namespace added by a GitHub installation, too.
            if (profile == "referat")
            {
                Directory.CreateDirectory(Path.Combine(project, "_extensions", "test-owner"));
                Directory.Move(Path.Combine(project, "_extensions", "academic"),
                    Path.Combine(project, "_extensions", "test-owner", "academic"));
            }
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
            Assert.DoesNotContain("ЗАГЛУШКА", text);
            Assert.Contains("АННОТАЦИЯ", text);
            Assert.Contains("DOCPROPERTY AcademicFigures", facts.Fields);
            Assert.Contains("DOCPROPERTY AcademicTables", facts.Fields);
            Assert.Contains("DOCPROPERTY AcademicSources", facts.Fields);
            Assert.Contains("DOCPROPERTY AcademicAppendices", facts.Fields);
            Assert.Equal(2, mainPart.Document.Descendants<W.SectionProperties>().Count());
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".quarto", "main.docx.finalize.json")));
            var annotation = report.RootElement.GetProperty("annotation");
            Assert.Equal(4, annotation.GetProperty("figures").GetInt32());
            Assert.Equal(3, annotation.GetProperty("tables").GetInt32());
            Assert.Equal(3, annotation.GetProperty("sources").GetInt32());
            Assert.Equal(2, annotation.GetProperty("appendices").GetInt32());
            var front = report.RootElement.GetProperty("front_matter");
            Assert.Equal(1, front.GetProperty("title_end").GetInt32());
            Assert.Equal(profile == "coursework" ? 4 : 2, front.GetProperty("annotation_start").GetInt32());
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
                Assert.Contains("ЗАДАНИЕ", text);
                Assert.Contains("5 Календарный план", text);
                Assert.Contains("УТВЕРЖДАЮ", text);
                // Fail before DOCX generation instead of silently blanking a sheet.
                File.WriteAllText(metadata, string.Join('\n', yaml.Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("summary:", StringComparison.Ordinal))));
                var missing = Run(project, "quarto", ["render"]);
                Assert.NotEqual(0, missing.Code);
                Assert.Contains("academic.summary", missing.Output);
                File.WriteAllText(metadata, Regex.Replace(yaml, @"(?ms)^    questions:.*?(?=^    calendar:)", "    questions: []\n"));
                var emptyQuestions = Run(project, "quarto", ["render"]);
                Assert.NotEqual(0, emptyQuestions.Code);
                Assert.Contains("academic.assignment.questions", emptyQuestions.Output);
                File.WriteAllText(metadata, yaml.Replace("due: \"15.10.2026\"", "due: \"\""));
                var missingDue = Run(project, "quarto", ["render"]);
                Assert.NotEqual(0, missingDue.Code);
                Assert.Contains("academic.assignment.calendar", missingDue.Output);
            }
            else
            {
                Assert.DoesNotContain("5 Календарный план", text);
                Assert.DoesNotContain("УТВЕРЖДАЮ", text);
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

    [WordFact]
    public void TechnicalCourseworkKeepsNativeFramesAndUpdatesStampTotalsAfterGrowth()
    {
        var source = TemplateSource();
        var reference = Path.GetFullPath(Path.Combine(source, "..", "Workspaces", "Пример"));
        var project = Path.Combine(Environment.GetEnvironmentVariable("ACADEMIC_TEST_ARTIFACTS") ?? Path.GetTempPath(), "ANK technical " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        MustRun(project, "quarto", ["use", "template", source, "--no-prompt"]);
        var yaml = File.ReadAllText(Path.Combine(reference, "metadata.yml"));
        if (!yaml.Contains("profile: coursework-susu-04-2008", StringComparison.Ordinal)) throw new InvalidDataException("Reference example must exercise the technical profile");
        File.WriteAllText(Path.Combine(project, "metadata.yml"), yaml);
        var main = Path.Combine(project, "chapters", "main.qmd");
        File.Copy(Path.Combine(reference, "chapters", "main.qmd"), main, true);
        File.Copy(Path.Combine(reference, "references.bib"), Path.Combine(project, "references.bib"), true);
        MustRun(project, "Rscript", ["--vanilla", "tools/install.R"]);
        var pageCounts = new List<int>();
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                var paragraph = string.Concat(Enumerable.Repeat("Дополнительный материал для проверки автоматического переноса текста и повторения основной надписи. ", 18));
                File.WriteAllText(main, File.ReadAllText(main).Replace("# Заключение", string.Join("\n\n", Enumerable.Repeat(paragraph, 8)) + "\n\n# Заключение", StringComparison.Ordinal));
            }
            MustRun(project, "quarto", ["render"]);
            var output = Path.Combine(project, "_output", "chapters", "main.docx");
            Assert.Empty(DocxInspection.Validate(output).Errors);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".quarto", "main.docx.finalize.json")));
            var pages = report.RootElement.GetProperty("pages").GetInt32();
            pageCounts.Add(pages);
            Assert.Equal(pages, report.RootElement.GetProperty("page_layout").GetProperty("stamp_total_pages").GetInt32());
            Assert.Equal(4, report.RootElement.GetProperty("page_layout").GetProperty("first_stamped_page").GetInt32());
            Assert.Equal(3, report.RootElement.GetProperty("annotation").GetProperty("tables").GetInt32());
            using (var package = WordprocessingDocument.Open(output, false))
            {
                var part = package.MainDocumentPart!;
                Assert.Equal(3, part.Document.Descendants<W.SectionProperties>().Count());
                Assert.Contains(part.HeaderParts, h => h.Header.InnerText.Contains("Университет", StringComparison.Ordinal));
                Assert.All(part.HeaderParts, h => Assert.DoesNotContain("Павлов", h.Header.InnerText));
                Assert.Contains(part.HeaderParts, h => h.Header.Descendants<W.SdtRun>().Any(s => s.SdtProperties?.GetFirstChild<W.Tag>()?.Val == "AcademicStamp.document-code"));
            }
            File.Copy(output, Path.Combine(project, pass == 0 ? "baseline.docx" : "expanded.docx"), true);
            File.Copy(Path.Combine(project, ".quarto", "main.docx.finalize.json"), Path.Combine(project, pass == 0 ? "baseline.json" : "expanded.json"), true);
        }
        Assert.True(pageCounts[1] > pageCounts[0], "Added text must increase the physical page count and stamp total");
        File.WriteAllText(Path.Combine(project, "metadata.yml"), yaml.Replace("profile: coursework-susu-04-2008", "profile: coursework-susu-04-2008\n  standard: unsupported-standard", StringComparison.Ordinal));
        var invalid = Run(project, "quarto", ["render"]);
        Assert.NotEqual(0, invalid.Code);
        Assert.Contains("academic.standard conflicts", invalid.Output);
        File.WriteAllText(Path.Combine(project, "metadata.yml"), yaml);
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
