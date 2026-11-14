using System.Diagnostics;
using System.Text.Json;
using AcademicDocx.Core;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

[Collection("Word")]
public sealed class HeadingLayoutTests
{
    [Theory]
    [InlineData("reference.docx")]
    [InlineData("reference-monochrome.docx")]
    public void ShippedReferencesHaveTheSameChapterAndCaptionLayout(string reference)
    {
        using var doc = WordprocessingDocument.Open(Path.Combine(AppContext.BaseDirectory, reference), false);
        var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!;
        W.StyleParagraphProperties Props(string id) => styles.Elements<W.Style>().Single(s => s.StyleId == id).StyleParagraphProperties!;
        Assert.True(Props("Heading1").PageBreakBefore!.Val!.Value);
        Assert.Equal(W.JustificationValues.Center, Props("Heading1").Justification!.Val!.Value);
        Assert.Equal("0", Props("Heading1").Indentation!.FirstLine!.Value);
        Assert.Equal("360", Props("Heading1").SpacingBetweenLines!.After!.Value);
        Assert.False(Props("Heading2").PageBreakBefore!.Val!.Value);
        Assert.Equal("397", Props("Heading2").Indentation!.FirstLine!.Value);
        Assert.Equal("340", Props("TableCaption").SpacingBetweenLines!.Before!.Value);
        Assert.Equal("120", Props("TableCaption").SpacingBetweenLines!.After!.Value);
        Assert.Equal("120", Props("ImageCaption").SpacingBetweenLines!.Before!.Value);
        Assert.Equal("340", Props("ImageCaption").SpacingBetweenLines!.After!.Value);
    }

    [WordFact]
    public void ShortChaptersStartSeparatePagesWhileSubsectionsStayWithTheirChapter()
    {
        var parent = Environment.GetEnvironmentVariable("ACADEMIC_TEST_ARTIFACTS") ?? Path.GetTempPath();
        var folder = Path.Combine(parent, "academic-heading-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "chapters.docx");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), file);
            using (var doc = WordprocessingDocument.Open(file, true))
            {
                var body = doc.MainDocumentPart!.Document.Body!;
                var section = (W.SectionProperties)body.Elements<W.SectionProperties>().Single().CloneNode(true);
                body.RemoveAllChildren();
                W.Paragraph P(string style, string text) => new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text)));
                body.Append(P("Heading1", "Первая глава"), P("BodyText", "Короткий текст первой главы."),
                    P("Heading2", "Подраздел первой главы"), P("BodyText", "Подраздел продолжается на этой же странице."),
                    P("Heading1", "Вторая глава"), P("BodyText", "Вторая глава начинается с нового листа."), section);
            }
            var report = Path.Combine(folder, "report.json");
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE")!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "finalize-word", file, "--report", report }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000)) { process.Kill(); throw new TimeoutException("Heading Word check exceeded two minutes"); }
            Assert.True(process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            using var result = JsonDocument.Parse(File.ReadAllText(report));
            Assert.True(result.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal(2, result.RootElement.GetProperty("pages").GetInt32());
            Assert.Empty(DocxInspection.Validate(file).Errors);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("ACADEMIC_TEST_ARTIFACTS") is null) Directory.Delete(folder, true);
        }
    }
}
