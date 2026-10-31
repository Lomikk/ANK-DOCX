using AcademicDocx.Core;

namespace AcademicDocx.Tests;

public sealed class BuildDiagnosticsTests
{
    [Fact]
    public void InspectMapsOnlyUnambiguousCellsToOriginalIncludedFiles()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ank-inspect"));
        var json = """
            {"fileInformation":{"chapters/main.qmd":{"includeMap":[{"source":"main.qmd","target":"_results.qmd"}],"codeCells":[
              {"file":"chapters/_results.qmd","start":4,"end":8,"source":"calculate()\nmore()","metadata":{"label":"tbl-results"}},
              {"file":"chapters/_code.qmd","start":5,"end":9,"source":"x <- 1","metadata":{"label":"run","lst-label":"lst-code"}},
              {"file":"chapters/_other.qmd","start":0,"end":2,"source":"x()","metadata":{"label":"duplicate"}},
              {"file":"chapters/_results.qmd","start":10,"end":12,"source":"y()","metadata":{"label":"duplicate"}}
            ]}}}
            """;
        var lines = DiagnosticSources.AddLocations([
            "Рекомендация: перед таблицей tbl-results не найдено упоминание.",
            "Рекомендация: перед листингом lst-code не найдено упоминание.",
            "Quitting from main.qmd:25-29 [tbl-results]",
            "Quitting from main.qmd:40-42 [duplicate]",
            "Рекомендация: раздел «Модель» начинается сразу с подраздела."
        ], json, root);
        Assert.Contains("chapters/_results.qmd:5-9", lines[0]);
        Assert.Contains("начало кода: calculate()", lines[0]);
        Assert.Contains("chapters/_code.qmd:6-10", lines[1]);
        Assert.StartsWith("Quitting from chapters/_results.qmd:5-9 [tbl-results]", lines[2]);
        Assert.Contains("строки объединённого документа; исходный файл не определён", lines[3]);
        Assert.DoesNotContain("ячейка:", lines[4]);
    }

    [Fact]
    public void ToolWarningsAndSourceLocationsSurviveWithoutMetadataOrStacks()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "metadata\ntitle: Example\nacademic:\n  group: private-group\n" +
                "\u001b[33mWARNING (main.lua:1) Unable to resolve crossref @fig-missing\u001b[39m\n" +
                "[WARNING] Citeproc: citation missing not found\n" +
                "Error in failing-cell: computation failed\nCalls: implementation -> details\n" +
                "Quitting from lines 7-9 [failing-cell] (chapters/main.qmd)\n" +
                "\nQuitting from main.qmd:1-4 [syntax-check]\n\n" +
                "Error running filter\nprofile.lua:22: Missing metadata: academic.title\nstack traceback:\n internal details\n" +
                "Output created: example.docx\n");
            var messages = string.Join('\n', BuildDiagnostics.Read(file));
            Assert.Contains("@fig-missing", messages);
            Assert.Contains("Citeproc: citation missing", messages);
            Assert.Contains("computation failed", messages);
            Assert.Contains("chapters/main.qmd", messages);
            Assert.Contains("main.qmd:1-4 [syntax-check]", messages);
            Assert.Contains("academic.title", messages);
            Assert.DoesNotContain("private-group", messages);
            Assert.DoesNotContain("internal details", messages);
            Assert.DoesNotContain("Calls:", messages);
            Assert.DoesNotContain('\u001b', messages);
        }
        finally { File.Delete(file); }
    }
}
