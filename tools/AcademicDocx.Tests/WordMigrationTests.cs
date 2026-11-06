using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AcademicDocx.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

public sealed class WordFactAttribute : FactAttribute
{
    public WordFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ACADEMIC_RUN_WORD_TESTS") != "1")
            Skip = "Set ACADEMIC_RUN_WORD_TESTS=1 and ACADEMIC_DOCX_SERVICE to the published service on a Windows machine with Word";
    }
}

public sealed class WordTheoryAttribute : TheoryAttribute
{
    public WordTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ACADEMIC_RUN_WORD_TESTS") != "1")
            Skip = "Set ACADEMIC_RUN_WORD_TESTS=1 and ACADEMIC_DOCX_SERVICE to the published service on a Windows machine with Word";
    }
}

[CollectionDefinition("Word", DisableParallelization = true)]
public sealed class WordCollection;

[Collection("Word")]
public sealed class WordMigrationTests : IDisposable
{
    private readonly string folder = Path.Combine(Environment.GetEnvironmentVariable("ACADEMIC_TEST_ARTIFACTS") ?? Path.GetTempPath(), "academic-word-tests-" + Guid.NewGuid().ToString("N"));
    public WordMigrationTests() => Directory.CreateDirectory(folder);
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [WordFact]
    public void TableNearBottomDoesNotCreateAnEmptySpacerPage()
    {
        var input = Path.Combine(folder, "table-bottom.docx");
        ReferenceStyles.Apply(Path.Combine(AppContext.BaseDirectory, "reference.docx"), input);
        using (var package = WordprocessingDocument.Open(input, true))
        {
            var body = package.MainDocumentPart!.Document.Body!;
            var section = (W.SectionProperties)body.Elements<W.SectionProperties>().Last().CloneNode(true);
            body.RemoveAllChildren();
            body.Append(new W.Table(new W.TableProperties(), new W.TableGrid(new W.GridColumn { Width = "9922" }),
                new W.TableRow(new W.TableRowProperties(new W.TableRowHeight { Val = 14000, HeightType = W.HeightRuleValues.Exact },
                    new W.CantSplit()), new W.TableCell(new W.Paragraph(new W.Run(new W.Text("TABLE CONTENT")))))));
            body.Append(new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "BodyText" }),
                new W.Run(new W.Text("AFTER TABLE"))), section);
        }
        var report = Finalize(input, expectedSuccess: true);
        Assert.Equal(2, report.GetProperty("pages").GetInt32());
        using var result = WordprocessingDocument.Open(input, false);
        var paragraphs = result.MainDocumentPart!.Document.Body!.Elements<W.Paragraph>().ToArray();
        Assert.Single(paragraphs);
        Assert.Equal("AFTER TABLE", paragraphs[0].InnerText);
        Assert.Equal("340", paragraphs[0].ParagraphProperties!.SpacingBetweenLines!.Before!.Value);
    }

    [WordFact]
    public void MergedHeadersMathAndFootersSurviveCompleteFinalization()
    {
        var input = Path.Combine(folder, "book.docx");
        File.Copy(Fixture("book-before-word.docx"), input);
        var before = DocxInspection.Read(input);
        var report = Finalize(input, expectedSuccess: true);
        var after = DocxInspection.Read(input);
        Assert.Equal(11, report.GetProperty("pages").GetInt32());
        Assert.Equal(5, after.Tables.Count);
        Assert.Equal(before.MathCount, after.MathCount);
        Assert.Equal(before.SequenceCount, after.SequenceCount);
        Assert.True(DocxInspection.SameRows(before.Tables[0].Rows, after.Tables[0].Rows));
        Assert.True(DocxInspection.SameRows(before.Tables[2].Rows, after.Tables[4].Rows));
        var longParts = after.Tables.Skip(1).Take(3).ToArray();
        // Row boundaries depend on field-code display. Require valid
        // fragments and complete logical rows rather
        // than the retired worker's view-dependent 17/28/10 body-row split.
        var combined = longParts[0].Rows.Concat(longParts.Skip(1).SelectMany(table => table.Rows.Skip(table.HeaderRows)));
        Assert.True(DocxInspection.SameRows(before.Tables[1].Rows, combined));
        foreach (var part in longParts)
        {
            Assert.Equal(before.Tables[1].HeaderShape, part.HeaderShape);
            Assert.Equal(2, part.HeaderRows);
            Assert.Equal(longParts[0].GridWidths, part.GridWidths);
        }
        AssertCaptionOrder(input, "Продолжение таблицы 2", "Окончание таблицы 2");
    }

    [WordTheory]
    [InlineData(12, 1)]
    [InlineData(48, 2)]
    [InlineData(90, 3)]
    public void RectangularTableHasCorrectLastCaptionAndPreservesRows(int count, int expectedParts)
    {
        var input = Pagination(count);
        var before = DocxInspection.Read(input).Tables.Single();
        Finalize(input, expectedSuccess: true);
        var after = DocxInspection.Read(input);
        Assert.Equal(expectedParts, after.Tables.Count);
        var combined = after.Tables[0].Rows.Concat(after.Tables.Skip(1).SelectMany(table => table.Rows.Skip(table.HeaderRows)));
        Assert.True(DocxInspection.SameRows(before.Rows, combined));
        Assert.All(after.Tables, table => Assert.Equal(before.HeaderShape, table.HeaderShape));
        var captions = expectedParts switch
        {
            1 => Array.Empty<string>(),
            2 => ["Окончание таблицы 1"],
            _ => new[] { "Продолжение таблицы 1", "Окончание таблицы 1" }
        };
        AssertCaptionOrder(input, captions);
    }

    [WordFact]
    public void TableReflowsAfterPrecedingContentChanges()
    {
        var input = Pagination(90);
        using (var package = WordprocessingDocument.Open(input, true))
            for (var index = 0; index < 14; index++)
                package.MainDocumentPart!.Document.Body!.PrependChild(new W.Paragraph(new W.Run(new W.Text("Additional preceding text for pagination verification."))));
        var before = DocxInspection.Read(input).Tables.Single();
        var report = Finalize(input, expectedSuccess: true);
        var after = DocxInspection.Read(input);
        Assert.True(after.Tables.Count >= 3);
        Assert.True(report.GetProperty("pages").GetInt32() >= 3);
        Assert.True(DocxInspection.SameRows(before.Rows,
            after.Tables[0].Rows.Concat(after.Tables.Skip(1).SelectMany(table => table.Rows.Skip(table.HeaderRows)))));
        AssertCaptionOrder(input, Enumerable.Repeat("Продолжение таблицы 1", after.Tables.Count - 2)
            .Append("Окончание таблицы 1").ToArray());
    }

    [WordTheory]
    [InlineData("missing-header", "needs a repeating header")]
    [InlineData("vertical-body-merge", "vertically merged body cells")]
    [InlineData("nested-table", "Nested tables")]
    [InlineData("oversized-row", "pages")]
    public void UnsupportedTablesFailWithoutReplacingInput(string kind, string message)
    {
        var input = Pagination(90);
        using (var package = WordprocessingDocument.Open(input, true))
        {
            var table = package.MainDocumentPart!.Document.Body!.Elements<W.Table>().Single();
            var rows = table.Elements<W.TableRow>().ToArray();
            switch (kind)
            {
                case "missing-header": rows[0].TableRowProperties!.RemoveAllChildren<W.TableHeader>(); break;
                case "vertical-body-merge":
                    (rows[1].Elements<W.TableCell>().First().TableCellProperties ??= new W.TableCellProperties())
                        .Append(new W.VerticalMerge { Val = W.MergedCellValues.Restart });
                    (rows[2].Elements<W.TableCell>().First().TableCellProperties ??= new W.TableCellProperties())
                        .Append(new W.VerticalMerge());
                    break;
                case "nested-table":
                    rows[1].Elements<W.TableCell>().First().Append(new W.Table(new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Nested")))))));
                    rows[1].Elements<W.TableCell>().First().Append(new W.Paragraph());
                    break;
                case "oversized-row":
                    var cell = rows[1].Elements<W.TableCell>().First();
                    for (var index = 0; index < 100; index++) cell.Append(new W.Paragraph(new W.Run(new W.Text("Very tall row"))));
                    break;
            }
        }
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        var report = Finalize(input, expectedSuccess: false);
        Assert.Contains(message, report.GetProperty("error").GetString());
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(input)));
        Assert.Empty(Directory.EnumerateFiles(folder, ".academic-*.docx"));
    }

    private string Pagination(int rows)
    {
        var input = Path.Combine(folder, "table.docx");
        File.Copy(Fixture("pagination.docx"), input);
        using var package = WordprocessingDocument.Open(input, true);
        foreach (var row in package.MainDocumentPart!.Document.Body!.Elements<W.Table>().Single().Elements<W.TableRow>().Skip(rows + 1).ToArray()) row.Remove();
        return input;
    }

    private JsonElement Finalize(string path, bool expectedSuccess)
    {
        var reportPath = Path.Combine(folder, "report.json");
        var executable = Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE")
            ?? throw new InvalidOperationException("ACADEMIC_DOCX_SERVICE must point to the published executable");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "finalize-word", path, "--report", reportPath }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000)) { process.Kill(); throw new TimeoutException("Word worker exceeded two minutes"); }
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        Assert.True(expectedSuccess ? process.ExitCode == 0 : process.ExitCode != 0, output);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement.Clone();
        Assert.Equal(expectedSuccess, root.GetProperty("passed").GetBoolean());
        var lifecycle = root.GetProperty("word");
        Assert.True(lifecycle.GetProperty("process_exited").GetBoolean());
        Assert.Empty(lifecycle.GetProperty("cleanup_errors").EnumerateArray());
        if (expectedSuccess) Assert.Equal(0, root.GetProperty("validation").GetProperty("error_count").GetInt32());
        return root;
    }

    private static void AssertCaptionOrder(string path, params string[] expected)
    {
        using var package = WordprocessingDocument.Open(path, false);
        foreach (var paragraph in package.MainDocumentPart!.Document.Body!.Elements<W.Paragraph>().Where(p =>
            p.InnerText.StartsWith("Продолжение таблицы", StringComparison.Ordinal) ||
            p.InnerText.StartsWith("Окончание таблицы", StringComparison.Ordinal)))
        {
            var before = paragraph.ParagraphProperties?.SpacingBetweenLines?.Before?.Value;
            var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "Normal";
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (before is null && visited.Add(id))
            {
                var style = package.MainDocumentPart.StyleDefinitionsPart?.Styles?.Elements<W.Style>().FirstOrDefault(s => s.StyleId == id);
                before = style?.StyleParagraphProperties?.SpacingBetweenLines?.Before?.Value;
                id = style?.BasedOn?.Val?.Value ?? "";
            }
            // Word omits a direct zero when the style already supplies zero.
            Assert.Equal("0", before ?? "0");
        }
        var captions = package.MainDocumentPart!.Document.Body!.Elements<W.Paragraph>().Select(p => string.Concat(p.Descendants<W.Text>().Select(text => text.Text)))
            .Where(text => text.StartsWith("Продолжение таблицы", StringComparison.Ordinal) || text.StartsWith("Окончание таблицы", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expected, captions);
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("ACADEMIC_TEST_ARTIFACTS") is null) Directory.Delete(folder, true);
    }
}
