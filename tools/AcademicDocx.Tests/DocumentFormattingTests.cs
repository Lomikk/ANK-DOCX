using AcademicDocx.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;

namespace AcademicDocx.Tests;

public sealed class DocumentFormattingTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "academic-formatting-" + Guid.NewGuid().ToString("N") + ".docx");

    [Fact]
    public void FormatsPlainAndMathTableTextWithoutChangingContentsOrFields()
    {
        Create();
        DocumentFormatting.Apply(path);
        Assert.Empty(DocxInspection.Validate(path).Errors);
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var table = body.Elements<W.Table>().Single();
        Assert.Equal("параметрx2", table.InnerText);
        Assert.All(table.Descendants<W.RunProperties>(), properties =>
        {
            Assert.Equal("24", properties.GetFirstChild<W.FontSize>()!.Val!.Value);
            Assert.Equal("24", properties.GetFirstChild<W.FontSizeComplexScript>()!.Val!.Value);
        });
        Assert.All(table.Descendants<M.Run>(), run => Assert.NotNull(run.GetFirstChild<W.RunProperties>()));
        Assert.All(table.Descendants<M.ControlProperties>(), control => Assert.NotNull(control.GetFirstChild<W.RunProperties>()));
        var inline = body.Elements<W.Paragraph>().Last();
        Assert.Equal("28", inline.Descendants<W.FontSize>().Single().Val!.Value);
        Assert.Single(inline.Elements<M.OfficeMath>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CentersDisplayObjectsPreservingNumberAtRightAndIsIdempotent(bool pandocDisplay)
    {
        Create(pandocDisplay);
        DocumentFormatting.Apply(path);
        string first;
        using (var doc = WordprocessingDocument.Open(path, false))
        {
            var body = doc.MainDocumentPart!.Document.Body!;
            var caption = body.Elements<W.Paragraph>().First();
            Assert.Null(caption.ParagraphProperties!.Justification);
            Assert.Equal("ImageCaption", caption.ParagraphProperties.ParagraphStyleId!.Val!.Value);
            var equation = body.Elements<W.Paragraph>().ElementAt(1);
            var tabs = equation.ParagraphProperties!.Tabs!.Elements<W.TabStop>().ToArray();
            Assert.Equal(W.TabStopValues.Center, tabs[0].Val!.Value);
            Assert.Equal(4961, tabs[0].Position!.Value);
            Assert.Equal(W.TabStopValues.Right, tabs[1].Val!.Value);
            Assert.Equal(9922, tabs[1].Position!.Value);
            Assert.IsType<W.TabChar>(equation.Elements<W.Run>().First().FirstChild);
            Assert.Equal("x", equation.Elements<M.OfficeMath>().Single().InnerText);
            Assert.Equal(" SEQ Формула \\* ARABIC ", equation.Descendants<W.FieldCode>().Single().Text);
            var display = body.Elements<W.Paragraph>().ElementAt(2).Elements<M.Paragraph>().Single();
            Assert.Equal(M.JustificationValues.Center, display.GetFirstChild<M.ParagraphProperties>()!.GetFirstChild<M.Justification>()!.Val!.Value);
            first = body.OuterXml;
        }
        DocumentFormatting.Apply(path);
        using var again = WordprocessingDocument.Open(path, false);
        Assert.Equal(first, again.MainDocumentPart!.Document.Body!.OuterXml);
    }

    [Theory]
    [InlineData("Рисунок")]
    [InlineData("РисунокAppendixA")]
    public void RemovesCaptionAlignmentFromEveryPreWordPropertyBlock(string sequence)
    {
        Create();
        using (var doc = WordprocessingDocument.Open(path, true))
        {
            var caption = doc.MainDocumentPart!.Document.Body!.Elements<W.Paragraph>().First();
            caption.PrependChild(new W.ParagraphProperties(new W.Justification { Val = W.JustificationValues.Center }));
            caption.Descendants<W.FieldCode>().Single().Text = " SEQ " + sequence + " \\* ARABIC ";
        }
        DocumentFormatting.Apply(path);
        using var result = WordprocessingDocument.Open(path, false);
        var blocks = result.MainDocumentPart!.Document.Body!.Elements<W.Paragraph>().First().Elements<W.ParagraphProperties>().ToArray();
        Assert.Equal(2, blocks.Length); // Word owns the existing schema repair.
        Assert.All(blocks, block => Assert.Null(block.Justification));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactsOnlyTheEmptyStructuralParagraphAfterAFinalTable(bool existingParagraph)
    {
        Create();
        using (var doc = WordprocessingDocument.Open(path, true))
        {
            var body = doc.MainDocumentPart!.Document.Body!;
            body.Elements<W.Paragraph>().Last().Remove();
            body.InsertBefore(new W.BookmarkStart { Id = "1", Name = "appendix" }, body.Elements<W.Table>().Single());
            if (existingParagraph) body.InsertBefore(new W.Paragraph(), body.GetFirstChild<W.SectionProperties>());
            body.InsertBefore(new W.BookmarkEnd { Id = "1" }, body.GetFirstChild<W.SectionProperties>());
        }
        DocumentFormatting.Apply(path);
        Assert.Empty(DocxInspection.Validate(path).Errors);
        using var result = WordprocessingDocument.Open(path, false);
        var terminal = result.MainDocumentPart!.Document.Body!.Elements<W.Paragraph>().Last();
        Assert.Equal("2", terminal.ParagraphProperties!.GetFirstChild<W.ParagraphMarkRunProperties>()!.GetFirstChild<W.FontSize>()!.Val!.Value);
        Assert.Equal("20", terminal.ParagraphProperties.SpacingBetweenLines!.Line!.Value);
        Assert.Equal("параметрx2", result.MainDocumentPart.Document.Body.Elements<W.Table>().Single().InnerText);
    }

    private void Create(bool pandocDisplay = false)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        static M.OfficeMath Math() => new(new M.Run(new M.Text("x")));
        static W.Run Field(string code) => new(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin },
            new W.FieldCode(code) { Space = SpaceProcessingModeValues.Preserve },
            new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }, new W.Text("1"),
            new W.FieldChar { FieldCharType = W.FieldCharValues.End });
        var caption = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = "ImageCaption" }, new W.Justification { Val = W.JustificationValues.Left }),
            Field(" SEQ Рисунок \\* ARABIC "));
        var numbered = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Formula" }),
            pandocDisplay ? new M.Paragraph(Math()) : Math(), new W.Run(new W.TabChar()), Field(" SEQ Формула \\* ARABIC "));
        var display = new W.Paragraph(new M.Paragraph(new M.ParagraphProperties(
            new M.Justification { Val = M.JustificationValues.Left }), Math()));
        var cell = new W.Paragraph(new W.Run(new W.RunProperties(new W.FontSize { Val = "28" }), new W.Text("параметр")),
            new M.OfficeMath(new M.Superscript(
                new M.SuperscriptProperties(new M.ControlProperties()),
                new M.Base(new M.Run(new M.Text("x"))), new M.SuperArgument(new M.Run(new M.Text("2"))))));
        var table = new W.Table(new W.TableProperties(), new W.TableGrid(new W.GridColumn { Width = "9922" }),
            new W.TableRow(new W.TableCell(cell)));
        var inline = new W.Paragraph(new W.Run(new W.RunProperties(new W.FontSize { Val = "28" }), new W.Text("текст")), Math());
        doc.AddMainDocumentPart().Document = new W.Document(new W.Body(caption, numbered, display, table, inline,
            new W.SectionProperties(new W.PageSize { Width = 11906, Height = 16838 },
                new W.PageMargin { Left = 1417, Right = 567, Top = 1134, Bottom = 1474, Header = 708, Footer = 1134, Gutter = 0 })));
    }

    public void Dispose() => File.Delete(path);
}
