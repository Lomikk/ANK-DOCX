using AcademicDocx.Core;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

public sealed class AppendixFormattingTests
{
    [Theory]
    [InlineData("basic")]
    [InlineData("research")]
    public void AppendixHeadingsAndPageSectionsRemainStableWhenReapplied(string mode)
    {
        var file = Path.Combine(Path.GetTempPath(), "academic-appendices-" + Guid.NewGuid().ToString("N") + ".docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), file);
        try
        {
            using (var package = WordprocessingDocument.Open(file, true))
            {
                var body = package.MainDocumentPart!.Document.Body!;
                var section = (W.SectionProperties)body.Elements<W.SectionProperties>().Single().CloneNode(true);
                body.RemoveAllChildren();
                W.Paragraph P(string style, string text = "") => new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text)));
                body.Append(P("AcademicResearchTitle", "Название"), P("AcademicTitleEnd"),
                    P("AcademicSummaryHeading", "РЕФЕРАТ"), P("AcademicBodyStart"),
                    new W.Paragraph(new W.Run(new W.FieldCode(" TOC \\o \"1-3\" \\h \\u "))), P("Normal", "Основной текст"));
                body.Append(new W.BookmarkStart { Name = "sec_app_a", Id = "101" }, P("Heading1", "Первая тема"),
                    new W.BookmarkEnd { Id = "101" }, P("Normal", "Материал А"),
                    new W.BookmarkStart { Name = "sec_app_b", Id = "102" }, P("Heading1", "Вторая тема"),
                    new W.BookmarkEnd { Id = "102" }, P("Normal", "Материал Б"), section);
            }
            var options = new PageLayoutOptions("susu-19-2008", "research-susu-19-2008", "Название", "КОД", "Автор", "Руководитель", "", "", "Вуз", "plain-body", mode,
                [new("sec-app-a", "А", "Первая тема", "mandatory"), new("sec-app-b", "Б", "Вторая тема", "informational")]);
            for (var pass = 0; pass < 2; pass++)
            {
                AppendixFormatting.Apply(file, options);
                Assert.Equal(2, FrontMatterFormatting.Apply(file)!.Appendices);
                DocumentPageLayout.Apply(file, options);
                Assert.Empty(DocxInspection.Validate(file).Errors);
                using var saved = WordprocessingDocument.Open(file, false);
                var main = saved.MainDocumentPart!;
                var sections = main.Document.Descendants<W.SectionProperties>().ToArray();
                Assert.Equal(mode == "research" ? 4 : 2, sections.Length);
                Assert.Contains("Материал А", main.Document.InnerText);
                Assert.Contains("Материал Б", main.Document.InnerText);
                if (mode != "research") continue;
                Assert.All(main.Document.Descendants<W.Paragraph>().Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "AcademicAppendixLabel"),
                    p => Assert.Equal(9, p.ParagraphProperties!.OutlineLevel!.Val!.Value));
                Assert.Equal(1, main.Document.Descendants<W.Paragraph>().Count(p => p.InnerText == "ПРИЛОЖЕНИЯ"));
                Assert.Contains("(обязательное)", main.Document.InnerText);
                Assert.Contains("(информационное)", main.Document.InnerText);
                for (var index = 2; index < 4; index++)
                {
                    var first = sections[index].Elements<W.HeaderReference>().Single(h => h.Type?.Value == W.HeaderFooterValues.First);
                    Assert.Equal("", ((HeaderPart)main.GetPartById(first.Id!)).Header.InnerText);
                    var next = sections[index].Elements<W.HeaderReference>().Single(h => h.Type?.Value == W.HeaderFooterValues.Default);
                    Assert.Equal("Продолжение приложения " + (index == 2 ? "А" : "Б"), ((HeaderPart)main.GetPartById(next.Id!)).Header.InnerText);
                    Assert.Null(sections[index].GetFirstChild<W.PageNumberType>());
                    Assert.Equal(3, sections[index].Elements<W.FooterReference>().Count());
                }
            }
        }
        finally { File.Delete(file); }
    }
}
