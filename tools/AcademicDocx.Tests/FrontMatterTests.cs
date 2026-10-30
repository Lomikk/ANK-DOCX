using AcademicDocx.Core;
using DocumentFormat.OpenXml.Packaging;
using C = DocumentFormat.OpenXml.CustomProperties;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

public sealed class FrontMatterTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "academic-front-tests-" + Guid.NewGuid().ToString("N"));
    public FrontMatterTests() => Directory.CreateDirectory(folder);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrontSectionHidesNumbersAndCountsSemanticObjects(bool coursework)
    {
        var file = Path.Combine(folder, "form.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), file);
        using (var package = WordprocessingDocument.Open(file, true))
        {
            var body = package.MainDocumentPart!.Document.Body!;
            var section = body.Elements<W.SectionProperties>().Single();
            section.Remove();
            body.RemoveAllChildren();
            W.Paragraph P(string style, string text) => new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text)));
            W.Paragraph Field(string code) => new(new W.Run(new W.FieldCode(code)));
            var calendar = new W.Table(new W.TableRow(new W.TableCell(P("Compact", "Календарный план"))));
            body.Append(P(coursework ? "AcademicCourseworkTitle" : "AcademicReferatTitle", "Название"),
                P("AcademicTitleEnd", ""), P("AcademicSheetHeading", "АННОТАЦИЯ"),
                calendar,
                P("AcademicBodyStart", ""), Field("SEQ Рисунок \\* ARABIC"), Field("SEQ Таблица \\* ARABIC"),
                Field("SEQ ТаблицаAppendixA \\* ARABIC"), P("Bibliography", "Использованный источник"),
                P("Heading1", "ПРИЛОЖЕНИЕ А"), section);
            package.MainDocumentPart.Document.Save();
        }
        Assert.Equal(new AnnotationStatistics(1, 2, 1, 1), FrontMatterFormatting.Apply(file));
        Assert.Equal(new AnnotationStatistics(1, 2, 1, 1), FrontMatterFormatting.Apply(file));
        using var saved = WordprocessingDocument.Open(file, false);
        var main = saved.MainDocumentPart!;
        var sections = main.Document.Descendants<W.SectionProperties>().ToArray();
        Assert.Equal(2, sections.Length);
        Assert.Null(sections[1].GetFirstChild<W.PageNumberType>());
        var reference = sections[0].Elements<W.FooterReference>().Single(f => f.Type?.Value == W.HeaderFooterValues.Default);
        Assert.DoesNotContain(((FooterPart)main.GetPartById(reference.Id!)).Footer.Descendants<W.SimpleField>(), f => f.Instruction == "PAGE");
        Assert.Contains(main.FooterParts, p => p.Footer.Descendants<W.SimpleField>().Any(f => f.Instruction == "PAGE"));
        var boundary = main.Document.Descendants<W.Paragraph>().Single(p => p.ParagraphProperties?.GetFirstChild<W.SectionProperties>() is not null);
        Assert.Equal(coursework ? "AcademicBodyStart" : "AcademicTitleEnd", boundary.ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Equal("2", saved.CustomFilePropertiesPart!.Properties!.Elements<C.CustomDocumentProperty>().Single(p => p.Name == "AcademicTables").InnerText);
    }

    [Fact]
    public void LegacyDocumentsKeepTheirSectionsAndHaveNoInventedStatistics()
    {
        var file = Path.Combine(folder, "legacy.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), file);
        var before = File.ReadAllBytes(file);
        Assert.Null(FrontMatterFormatting.Apply(file));
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    public void Dispose() => Directory.Delete(folder, true);
}
