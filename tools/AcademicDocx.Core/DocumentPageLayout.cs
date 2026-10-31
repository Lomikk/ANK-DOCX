using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Core;

public sealed record PageLayoutOptions(string Standard, string Profile, string Title, string DocumentCode,
    string Author, string Supervisor, string NormalController, string Approver, string Organization, string PageLayout)
{
    public bool Technical => PageLayout == "technical";
    public static PageLayoutOptions Read(string path) => JsonSerializer.Deserialize<PageLayoutOptions>(File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing layout metadata");
}

/// <summary>Page sections and furniture shared by front matter and the entire body.</summary>
public static class DocumentPageLayout
{
    private static readonly XNamespace XW = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static void Set<T>(OpenXmlCompositeElement parent, T value) where T : OpenXmlElement
    {
        parent.RemoveAllChildren<T>(); parent.AddChild(value, true);
    }
    private static string Twips(double mm) => Math.Round(mm * 1440 / 25.4).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static void Apply(string path, PageLayoutOptions? options = null, string? templates = null)
    {
        if (options is not null && options.PageLayout is not ("technical" or "plain-body" or "plain-after-title"))
            throw new InvalidDataException("Unsupported page layout: " + options.PageLayout);
        using var package = WordprocessingDocument.Open(path, true);
        var main = package.MainDocumentPart!;
        var body = main.Document.Body!;
        static string Style(W.Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var paragraphs = body.Elements<W.Paragraph>().ToArray();
        var title = paragraphs.FirstOrDefault(p => Style(p) is "AcademicCourseworkTitle" or "AcademicReferatTitle");
        if (title is null) return;
        var markers = paragraphs.Where(p => Style(p) is "AcademicTitleEnd" or "AcademicAssignmentPageEnd" or "AcademicAssignmentEnd" or "AcademicBodyStart").ToArray();
        var final = body.Elements<W.SectionProperties>().Single();
        var technical = options?.Technical == true;
        var baseSection = (W.SectionProperties)final.CloneNode(true);
        foreach (var marker in markers)
        {
            var props = marker.ParagraphProperties!;
            props.RemoveAllChildren<W.SectionProperties>();
            Set(props, new W.SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = W.LineSpacingRuleValues.Exact });
            Set(props, new W.ParagraphMarkRunProperties(new W.FontSize { Val = "2" }));
            marker.RemoveAllChildren<W.Run>();
        }
        W.SectionProperties Section()
        {
            var section = (W.SectionProperties)baseSection.CloneNode(true);
            section.RemoveAllChildren<W.HeaderReference>(); section.RemoveAllChildren<W.FooterReference>();
            section.RemoveAllChildren<W.TitlePage>(); section.RemoveAllChildren<W.PageNumberType>();
            Set(section, new W.SectionType { Val = W.SectionMarkValues.NextPage });
            return section;
        }
        var emptyHeader = main.AddNewPart<HeaderPart>(); emptyHeader.Header = new W.Header(new W.Paragraph());
        FooterPart Footer(double? reservedMm = null)
        {
            var part = main.AddNewPart<FooterPart>();
            var pp = new W.ParagraphProperties();
            if (reservedMm.HasValue)
            {
                pp.SpacingBetweenLines = new W.SpacingBetweenLines { Before = "0", After = "0", Line = Twips(reservedMm.Value), LineRule = W.LineSpacingRuleValues.Exact };
                pp.ParagraphMarkRunProperties = new W.ParagraphMarkRunProperties(new W.FontSize { Val = "2" });
            }
            part.Footer = new W.Footer(new W.Paragraph(pp)); return part;
        }
        var emptyFooter = Footer();
        void Header(W.SectionProperties s, HeaderPart part, W.HeaderFooterValues kind) =>
            s.PrependChild(new W.HeaderReference { Type = kind, Id = main.GetIdOfPart(part) });
        void AddFooter(W.SectionProperties s, FooterPart part, W.HeaderFooterValues kind) =>
            s.PrependChild(new W.FooterReference { Type = kind, Id = main.GetIdOfPart(part) });
        var boundaries = new Dictionary<string, W.SectionProperties>();
        if (!technical)
        {
            var front = Section(); Header(front, emptyHeader, W.HeaderFooterValues.Default); AddFooter(front, emptyFooter, W.HeaderFooterValues.Default);
            var afterTitle = options is null ? Style(title) == "AcademicReferatTitle" : options.PageLayout == "plain-after-title";
            boundaries[afterTitle ? "AcademicTitleEnd" : "AcademicBodyStart"] = front;
            final.RemoveAllChildren<W.PageNumberType>();
        }
        else
        {
            if (templates is null) throw new InvalidDataException("STO 04 page templates are required");
            HeaderPart Frame(string name)
            {
                var xml = XElement.Load(Path.Combine(templates, name + ".xml"));
                var values = new Dictionary<string, string> { ["title"] = options!.Title, ["document-code"] = options.DocumentCode,
                    ["author"] = options.Author, ["supervisor"] = options.Supervisor, ["normal-controller"] = options.NormalController,
                    ["approver"] = options.Approver, ["organization"] = options.Organization };
                foreach (var control in xml.Descendants(XW + "sdt"))
                {
                    var tag = control.Element(XW + "sdtPr")?.Element(XW + "tag")?.Attribute(XW + "val")?.Value;
                    if (tag is null || !tag.StartsWith("AcademicStamp.", StringComparison.Ordinal)) continue;
                    var run = control.Element(XW + "sdtContent")!.Element(XW + "r")!;
                    run.Elements(XW + "t").Remove();
                    var lines = values[tag[14..]].Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) run.Add(new XElement(XW + "br"));
                        run.Add(new XElement(XW + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i]));
                    }
                }
                var part = main.AddNewPart<HeaderPart>(); part.Header = new W.Header(xml.ToString(SaveOptions.DisableFormatting)); return part;
            }
            var titleFrame = Frame("title"); var firstFrame = Frame("first"); var followingFrame = Frame("following");
            var titleSection = Section(); Header(titleSection, titleFrame, W.HeaderFooterValues.Default); AddFooter(titleSection, emptyFooter, W.HeaderFooterValues.Default);
            var assignmentSection = Section(); Header(assignmentSection, emptyHeader, W.HeaderFooterValues.Default); AddFooter(assignmentSection, emptyFooter, W.HeaderFooterValues.Default);
            boundaries["AcademicTitleEnd"] = titleSection; boundaries["AcademicAssignmentEnd"] = assignmentSection;
            final.RemoveAllChildren<W.HeaderReference>(); final.RemoveAllChildren<W.FooterReference>();
            final.RemoveAllChildren<W.PageNumberType>(); Set(final, new W.TitlePage());
            Header(final, firstFrame, W.HeaderFooterValues.First); Header(final, followingFrame, W.HeaderFooterValues.Default);
            // The inline blank footer reserves real layout space above the
            // independently anchored drawing: 40/15 mm stamp + 5 mm text gap.
            AddFooter(final, Footer(45), W.HeaderFooterValues.First); AddFooter(final, Footer(20), W.HeaderFooterValues.Default);
            foreach (var section in boundaries.Values.Append(final))
            {
                Set(section, new W.PageSize { Width = uint.Parse(Twips(210)), Height = uint.Parse(Twips(297)) });
                Set(section, new W.PageMargin { Top = int.Parse(Twips(20)), Bottom = int.Parse(Twips(20)),
                    Left = uint.Parse(Twips(25)), Right = uint.Parse(Twips(10)), Header = uint.Parse(Twips(5)), Footer = uint.Parse(Twips(5)) });
            }
        }
        foreach (var marker in markers)
        {
            if (boundaries.TryGetValue(Style(marker), out var section)) Set(marker.ParagraphProperties!, section);
            else marker.Append(new W.Run(new W.RunProperties(new W.FontSize { Val = "2" }), new W.Break { Type = W.BreakValues.Page }));
        }
        main.Document.Save();
    }
}
