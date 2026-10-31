using AcademicDocx.Core;

namespace AcademicDocx.Tests;

public sealed class BuildDiagnosticsTests
{
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
