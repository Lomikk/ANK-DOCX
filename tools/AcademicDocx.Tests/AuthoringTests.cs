using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

public sealed class QuartoFactAttribute : FactAttribute
{
    public QuartoFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Environment.GetEnvironmentVariable("ACADEMIC_QUARTO")))
            Skip = "Set ACADEMIC_QUARTO to the installed quarto.cmd for Pandoc/Quarto authoring checks";
    }
}

public sealed class AuthoringTests
{
    [QuartoFact]
    public void FormulaDefinitionsRequireSeparateLinesAndPreserveWordLineBreaks()
    {
        var root = Repository();
        var folder = Path.Combine(root, ".tools", "formula-definitions-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Copy(Path.Combine(root, "quarto", "_extensions"), Path.Combine(folder, "_extensions"));
        File.WriteAllText(Path.Combine(folder, "references.bib"), "");
        const string formula = "$$v=\\frac{s}{t},$$ {#eq-speed}\n\n";
        const string definitions = """
            ::: {custom-style="Formula Definitions"}
            | где $v$ — скорость, $м/с$;
            | $s$ — расстояние, м;
            | $t$ — время, с.
            :::
            """;
        var valid = Render(folder, formula + definitions);
        Assert.True(valid.Code == 0, valid.Output);
        using (var doc = WordprocessingDocument.Open(Path.Combine(folder, "index.docx"), false))
        {
            var style = Assert.Single(doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<W.Style>(),
                s => s.StyleName?.Val?.Value == "Formula Definitions");
            var paragraph = Assert.Single(doc.MainDocumentPart.Document.Body!.Descendants<W.Paragraph>(),
                p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == style.StyleId?.Value);
            Assert.Equal(2, paragraph.Descendants<W.Break>().Count());
            Assert.Equal(4, paragraph.Descendants<DocumentFormat.OpenXml.Math.OfficeMath>().Count());
            Assert.StartsWith("где", paragraph.InnerText, StringComparison.Ordinal);
        }
        foreach (var (content, error) in new[]
        {
            (formula + "Здесь $V$ — выручка; $B$ — активы.", "вместо обычного абзаца"),
            (formula + "где $v$ — скорость;\n$s$ — расстояние.", "вместо обычного абзаца"),
            (formula + "| где $v$ — скорость;\n| $s$ — расстояние.", "вместо обычного абзаца"),
            (formula + definitions.Replace("| где", "где").Replace("| $", "$"), "Markdown line block"),
            (formula + definitions.Replace("где $v$", "где: $v$"), "без двоеточия"),
            (formula + definitions.Replace("где $v$", "Здесь $v$"), "без двоеточия"),
            (formula + definitions.Replace("| $s$ — расстояние, м;", "| $s$ и $d$ — расстояния, м;"), "Не объединяйте"),
            (formula + definitions.Replace("| $s$ — расстояние, м;\n| $t$", "| $s$ — расстояние, м; $t$"), "отдельную строку"),
            (formula + definitions.Replace("| $s$ — расстояние, м;\n| $t$", "| $s$ — расстояние, м, $t$"), "отдельную строку"),
            (formula + definitions.Replace("| $s$ — расстояние, м;", "| $s$ — расстояние, м."), "точкой с запятой"),
            (formula + definitions.Replace("время, с.", "время, с;"), "точкой"),
            (formula + definitions.Replace("расстояние, м;", ";"), "расшифровку"),
            (formula + definitions.Replace("| $s$ —", "| где $s$ —"), "одно обозначение"),
            (formula + definitions.Replace("| $s$ —", "| $s$ -"), "одно обозначение"),
            (definitions.Replace("| где $v$ — скорость, $м/с$;", "| где $v$ и $u$ — скорости;"), "Не объединяйте")
        })
        {
            var rejected = Render(folder, content);
            Assert.True(rejected.Code != 0, "Unexpected success:\n" + content);
            Assert.Contains(error, rejected.Output);
            if (content.StartsWith(formula, StringComparison.Ordinal)) Assert.Contains("eq-speed", rejected.Output);
            var diagnosticLog = Path.Combine(folder, "diagnostic.log");
            File.WriteAllText(diagnosticLog, rejected.Output);
            var shortLog = string.Join('\n', AcademicDocx.Core.BuildDiagnostics.Read(diagnosticLog));
            Assert.Contains(error, shortLog);
            Assert.DoesNotContain("stack traceback:", shortLog);
        }
        foreach (var body in new[]
        {
            // Previously defined symbols do not require a repeated legend.
            "$v$ — скорость, $s$ — путь, $t$ — время.\n\n" + formula,
            // A following paragraph with math is not necessarily a definition.
            formula + "Здесь рассматривается случай $t>0$.",
            formula + "Для расчёта принято $t=10$.",
            // Source examples and ordinary line blocks are not formula legends.
            "```markdown\n" + formula + "Здесь $v$ — скорость.\n```",
            formula + "| Первая строка\n| Вторая строка",
            // Nested author content is validated through the standard Blocks walk.
            "::: {.example}\n\n" + formula + definitions + "\n\n:::"
        })
        {
            valid = Render(folder, body);
            Assert.True(valid.Code == 0, valid.Output);
        }
        File.WriteAllText(Path.Combine(folder, "_formula.qmd"), formula);
        var included = Render(folder, "{{< include _formula.qmd >}}\n\n" + definitions);
        Assert.True(included.Code == 0, included.Output);
        var includedError = Render(folder, "{{< include _formula.qmd >}}\n\nЗдесь $v$ — скорость.");
        Assert.NotEqual(0, includedError.Code);
        Assert.Contains("Пояснения формулы eq-speed", includedError.Output);
    }

    [QuartoFact]
    public void StandardQuartoAuthoringGeneratesBibliographyAndRejectsManualReplacements()
    {
        var root = Repository();
        var folder = Path.Combine(root, ".tools", "authoring-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Copy(Path.Combine(root, "quarto", "_extensions"), Path.Combine(folder, "_extensions"));
        File.WriteAllText(Path.Combine(folder, "references.bib"), "@book{real,title={Real source},author={Doe, Jane},year={2026}}\n");
        File.WriteAllText(Path.Combine(folder, "figure.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"60\"><rect width=\"90\" height=\"50\" fill=\"black\"/></svg>");
        File.Copy(Path.Combine(folder, "_extensions", "academic", "academic.docx"), Path.Combine(folder, "other.docx"));
        const string body = """
            # Chapter {#sec-chapter}

            Source [@real]. Formula @eq-example. Table @tbl-example. Figure @fig-example. Section @sec-chapter.

            $$x=1$$ {#eq-example}

            | Value |
            |------:|
            |     1 |

            : Values {#tbl-example}

            ![Plot](figure.svg){#fig-example}

            # Extra data {.appendix #sec-extra}

            Appendix text.
            """;
        var valid = Render(folder, body);
        Assert.True(valid.Code == 0, valid.Output);
        using (var doc = WordprocessingDocument.Open(Path.Combine(folder, "index.docx"), false))
        {
            var paragraphs = doc.MainDocumentPart!.Document.Body!.Descendants<W.Paragraph>().Select(p => p.InnerText).ToArray();
            Assert.Single(paragraphs, p => p == "Библиографический список");
            Assert.True(Array.FindIndex(paragraphs, p => p == "Библиографический список") < Array.FindIndex(paragraphs, p => p == "Extra data"));
            Assert.Contains(paragraphs, p => p.Contains("Real source"));
            Assert.DoesNotContain(paragraphs, p => p.StartsWith("1. ", StringComparison.Ordinal) && p.Contains("Real source"));
        }
        foreach (var (content, metadata, error) in new[]
        {
            (body, "toc-title: СОДЕРЖАНИЕ", "toc-title"),
            (body, "csl: references.bib", "csl"),
            (body, "format:\n  academic-docx:\n    reference-doc: other.docx", "reference-doc"),
            (body, "format:\n  academic-docx:\n    template: figure.svg", "template"),
            (body, "format:\n  academic-docx:\n    number-sections: true", "number-sections"),
            (body, "suppress-bibliography: true", "библиографию"),
            (body, "citeproc: false", "Citeproc"),
            (body, "reference-section-title: Manual", "reference-section-title"),
            (body, "references: [{id: invented, title: Invented}]", "references"),
            (body + "\n\n# ОГЛАВЛЕНИЕ\n", "", "создаёт сборщик"),
            (body + "\n\n# Библиографический список\n\n1. Manual source.\n", "", "создаёт сборщик"),
            (body + "\n\nОГЛАВЛЕНИЕ\n", "", "Сопроводительный"),
            (body + "\n\n::: {#refs}\n:::\n", "", "Блок refs"),
            (body.Replace("[@real]", "[@missing]"), "", "Источник @missing"),
            (body.Replace("@eq-example.", "@eq-missing."), "", "Объект @eq-missing"),
            (body.Replace("@eq-example.", "[-@eq-example]."), "", "без [-...]"),
            (body.Replace("@eq-example.", "(@eq-example)."), "", "внешние скобки"),
            (body.Replace("Source [@real].", "Source [1, с. 9]."), "", "ручная числовая ссылка")
        })
        {
            var rejected = Render(folder, content, metadata);
            Assert.True(rejected.Code != 0, "Unexpected success: " + metadata + "\n" + content);
            Assert.Contains(error, rejected.Output);
        }
        var math = Render(folder, "# Calculation\n\n$x[1]$ and `data[1]`.\n");
        Assert.True(math.Code == 0, math.Output);
        var monochrome = Render(folder, body, "format:\n  academic-docx:\n    reference-doc: _extensions/academic/academic-monochrome.docx");
        Assert.True(monochrome.Code == 0, monochrome.Output);
        Directory.CreateDirectory(Path.Combine(folder, "_extensions", "test-owner"));
        Directory.Move(Path.Combine(folder, "_extensions", "academic"), Path.Combine(folder, "_extensions", "test-owner", "academic"));
        var namespaced = Render(folder, body);
        Assert.True(namespaced.Code == 0, namespaced.Output);
        File.WriteAllText(Path.Combine(folder, "_quarto.yml"), """
            project:
              type: book
            format: academic-docx
            book:
              output-file: authoring-book
              chapters: [index.qmd, chapter.qmd]
              appendices: [appendix.qmd]
            """);
        File.WriteAllText(Path.Combine(folder, "chapter.qmd"), "# Analysis {#sec-analysis}\n\nSee @eq-book.\n\n$$y=2$$ {#eq-book}\n");
        File.WriteAllText(Path.Combine(folder, "appendix.qmd"), "# Supplement\n\nAdditional data.\n");
        var book = Render(folder, "# Introduction {.unnumbered}\n\nSource [@real]; equation @eq-book from chapter @sec-analysis.\n", book: true);
        Assert.True(book.Code == 0, book.Output);
        using var bookDoc = WordprocessingDocument.Open(Path.Combine(folder, "_book", "authoring-book.docx"), false);
        var bookText = bookDoc.MainDocumentPart!.Document.Body!.Descendants<W.Paragraph>().Select(p => p.InnerText).ToArray();
        Assert.Single(bookText, p => p == "Библиографический список");
        Assert.True(Array.FindIndex(bookText, p => p == "Библиографический список") < Array.FindIndex(bookText, p => p == "Supplement"));
    }

    private static (int Code, string Output) Render(string folder, string body, string metadata = "", bool book = false)
    {
        var source = """
            ---
            title: Test
            author: Test author
            format: academic-docx
            bibliography: references.bib
            academic:
              profile: practical-local
              parent-organization: Ministry
              university: University
              school: School
              department: Department
              discipline: Subject
              group: A1
              author-short: A. Author
              supervisor: Supervisor
              supervisor-position: Professor
              city: City
              year: 2026
            """ + "\n" + metadata + "\n---\n" + body;
        if (metadata.StartsWith("format:", StringComparison.Ordinal)) source = source.Replace("format: academic-docx", "", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(folder, "index.qmd"), source, new UTF8Encoding(false));
        var cmd = Environment.GetEnvironmentVariable("ACADEMIC_QUARTO")!;
        var info = new ProcessStartInfo(Path.ChangeExtension(cmd, ".exe")) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        info.Environment["PATH"] = Path.GetDirectoryName(cmd) + ";" + info.Environment["PATH"];
        info.ArgumentList.Add("render");
        if (!book) info.ArgumentList.Add("index.qmd");
        info.ArgumentList.Add("--no-clean");
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Quarto authoring test timed out"); }
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        File.AppendAllText(Path.Combine(folder, "tests.log"), metadata + "\n" + output + "\n");
        return (process.ExitCode, output);
    }

    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "quarto", "_extensions", "academic", "authoring.lua"))) return dir.FullName;
        throw new InvalidOperationException("Run authoring tests in the source checkout");
    }
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
