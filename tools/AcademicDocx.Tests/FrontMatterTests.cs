using AcademicDocx.Core;
using DocumentFormat.OpenXml.Packaging;
using C = DocumentFormat.OpenXml.CustomProperties;
using W = DocumentFormat.OpenXml.Wordprocessing;
using System.Xml.Linq;

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
        DocumentPageLayout.Apply(file);
        Assert.Equal(new AnnotationStatistics(1, 2, 1, 1), FrontMatterFormatting.Apply(file));
        DocumentPageLayout.Apply(file);
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

    [Fact]
    public void TechnicalLayoutUsesNormativeFramesAndDifferentFirstPageAfterAssignment()
    {
        var file = Path.Combine(folder, "technical.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), file);
        using (var package = WordprocessingDocument.Open(file, true))
        {
            var body = package.MainDocumentPart!.Document.Body!;
            var section = (W.SectionProperties)body.Elements<W.SectionProperties>().Single().CloneNode(true);
            body.RemoveAllChildren();
            W.Paragraph P(string style, string text = "") => new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text)));
            body.Append(P("AcademicCourseworkTitle", "Тема"), P("AcademicTitleEnd"), P("AcademicFormText", "Задание"),
                P("AcademicAssignmentPageEnd"), P("AcademicFormText", "План"), P("AcademicAssignmentEnd"),
                P("AcademicSheetHeading", "АННОТАЦИЯ"), P("AcademicBodyStart"), P("Normal", "Основной текст"), section);
        }
        FrontMatterFormatting.Apply(file);
        var options = new PageLayoutOptions("susu-04-2008", "coursework-susu-04-2008", "Тема", "КОД ПЗ КР", "А.А. Автор", "Б.Б. Руководитель", "В.В. Контролер", "", "Вуз\nГруппа", "technical");
        DocumentPageLayout.Apply(file, options, Path.Combine(AppContext.BaseDirectory, "PageTemplates"));
        using var result = WordprocessingDocument.Open(file, false);
        var main = result.MainDocumentPart!;
        var sections = main.Document.Descendants<W.SectionProperties>().ToArray();
        Assert.Equal(3, sections.Length);
        Assert.NotNull(sections[2].GetFirstChild<W.TitlePage>());
        Assert.Null(sections[2].GetFirstChild<W.PageNumberType>());
        var first = sections[2].Elements<W.HeaderReference>().Single(r => r.Type?.Value == W.HeaderFooterValues.First);
        var header = ((HeaderPart)main.GetPartById(first.Id!)).Header;
        Assert.Contains("КОД ПЗ КР", header.InnerText);
        Assert.DoesNotContain("Павлов", header.InnerText);
        Assert.DoesNotContain("Ботов", header.InnerText);
        Assert.DoesNotContain("82", header.InnerText);
        Assert.Contains(header.Descendants<W.SimpleField>(), f => f.Instruction == "PAGE");
        Assert.Contains(header.Descendants<W.SimpleField>(), f => f.Instruction == "NUMPAGES");
        Assert.All(main.HeaderParts.SelectMany(p => p.Header.Descendants<W.Text>())
            .Where(t => !t.Ancestors<W.TextBoxContent>().Any()), t => Assert.True(string.IsNullOrWhiteSpace(t.Text)));
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        var xml = XElement.Parse(header.OuterXml);
        var anchor = xml.Descendants(wp + "anchor").Single();
        Assert.Equal("720000", anchor.Element(wp + "positionH")!.Element(wp + "posOffset")!.Value);
        Assert.Equal("180000", anchor.Element(wp + "positionV")!.Element(wp + "posOffset")!.Value);
        Assert.Equal("6660000", anchor.Element(wp + "extent")!.Attribute("cx")!.Value);
        Assert.Equal("10332000", anchor.Element(wp + "extent")!.Attribute("cy")!.Value);
        Assert.Empty(DocxInspection.Validate(file).Errors);
    }

    [Fact]
    public void ResearchFormsShareStatisticsAndAllowAFlowingAssignment()
    {
        var file = Path.Combine(folder, "research.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), file);
        using (var package = WordprocessingDocument.Open(file, true))
        {
            var body = package.MainDocumentPart!.Document.Body!;
            var section = (W.SectionProperties)body.Elements<W.SectionProperties>().Single().CloneNode(true);
            body.RemoveAllChildren();
            W.Paragraph P(string style, string text = "") => new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text)));
            body.Append(P("AcademicResearchTitle", "НИР"), P("AcademicTitleEnd"), P("AcademicFormText", "Задание"),
                P("AcademicAssignmentEnd"), P("AcademicSummaryHeading", "РЕФЕРАТ"), P("AcademicBodyStart"),
                P("Normal", "Текст"), section);
        }
        Assert.Equal(new AnnotationStatistics(0, 0, 0, 0), FrontMatterFormatting.Apply(file));
        DocumentPageLayout.Apply(file, new PageLayoutOptions("susu-19-2008", "research-susu-19-2008", "НИР", "КОД", "Автор", "Руководитель", "", "", "Вуз", "plain-body"));
        using var saved = WordprocessingDocument.Open(file, false);
        Assert.Equal(2, saved.MainDocumentPart!.Document.Descendants<W.SectionProperties>().Count());
        Assert.DoesNotContain(saved.MainDocumentPart.Document.Descendants<W.Paragraph>(), p => p.ParagraphProperties?.ParagraphStyleId?.Val == "AcademicAssignmentPageEnd");
        Assert.Empty(DocxInspection.Validate(file).Errors);
    }

    public void Dispose() => Directory.Delete(folder, true);
}
