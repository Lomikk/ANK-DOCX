using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using C = DocumentFormat.OpenXml.CustomProperties;
using V = DocumentFormat.OpenXml.VariantTypes;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Core;

public sealed record AnnotationStatistics(int Figures, int Tables, int Sources, int Appendices);

/// <summary>Shared sheet styles and annotation statistics; page sections belong to DocumentPageLayout.</summary>
public static class FrontMatterFormatting
{
    private static void Set<T>(OpenXmlCompositeElement parent, T value) where T : OpenXmlElement
    {
        parent.RemoveAllChildren<T>();
        parent.AddChild(value, true);
    }

    public static AnnotationStatistics? Apply(string path)
    {
        using var package = WordprocessingDocument.Open(path, true);
        var main = package.MainDocumentPart!;
        var body = main.Document.Body!;
        static string Style(W.Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var paragraphs = body.Elements<W.Paragraph>().ToArray();
        var styles = main.StyleDefinitionsPart!.Styles!;
        var styleNames = styles.Elements<W.Style>().Where(s => s.StyleId?.Value is not null)
            .ToDictionary(s => s.StyleId!.Value!, s => s.StyleName?.Val?.Value?.Replace(" ", "", StringComparison.Ordinal) ?? "", StringComparer.Ordinal);
        string NamedStyle(W.Paragraph p) => styleNames.GetValueOrDefault(Style(p), Style(p));
        var title = paragraphs.FirstOrDefault(p => Style(p) is "AcademicCourseworkTitle" or "AcademicReferatTitle");
        if (title is null) return null; // Older documents keep their original section layout.
        void Define(string id, W.JustificationValues? alignment = null,
            int before = 0, int after = 0, int left = 0, bool bold = false, bool keepNext = false)
        {
            var style = styles.Elements<W.Style>().FirstOrDefault(s => s.StyleId == id);
            if (style is null)
            {
                style = new W.Style { Type = W.StyleValues.Paragraph, StyleId = id, StyleName = new W.StyleName { Val = id } };
                styles.Append(style);
            }
            Set(style, new W.BasedOn { Val = "Normal" });
            var rp = style.StyleRunProperties ??= new W.StyleRunProperties();
            Set(rp, new W.FontSize { Val = "28" });
            Set(rp, new W.FontSizeComplexScript { Val = "28" });
            Set(rp, new W.RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman", ComplexScript = "Times New Roman" });
            Set(rp, new W.Color { Val = "000000" });
            Set(rp, new W.Bold { Val = bold });
            var pp = style.StyleParagraphProperties ??= new W.StyleParagraphProperties();
            Set(pp, new W.Indentation { FirstLine = "0", Left = left.ToString(), Right = "0" });
            Set(pp, new W.Justification { Val = alignment ?? W.JustificationValues.Left });
            Set(pp, new W.SpacingBetweenLines { Before = (before * 20).ToString(), After = (after * 20).ToString(), Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
            Set(pp, new W.KeepNext { Val = keepNext });
            Set(pp, new W.KeepLines());
            Set(pp, new W.OutlineLevel { Val = 9 }); // The forms are excluded from the body TOC.
            Set(pp, new W.SuppressAutoHyphens());
        }
        Define("AcademicInstitution", W.JustificationValues.Center, keepNext: true);
        Define("AcademicCourseworkTitle", W.JustificationValues.Center, before: 35, after: 28, keepNext: true);
        Define("AcademicReferatTitle", W.JustificationValues.Center, before: 56, after: 28, keepNext: true);
        Define("AcademicWorkType", W.JustificationValues.Center, bold: true, keepNext: true);
        Define("AcademicCentered", W.JustificationValues.Center, keepNext: true);
        Define("AcademicSignColumns");
        var columns = styles.Elements<W.Style>().Single(s => s.StyleId == "AcademicSignColumns");
        Set(columns.StyleParagraphProperties!, new W.Tabs(new W.TabStop { Val = W.TabStopValues.Left, Position = 5100 }));
        Define("AcademicRightBlock", left: 5100);
        Define("AcademicRightBlockStart", before: 14, left: 5100, keepNext: true);
        Define("AcademicTitlePlace", W.JustificationValues.Center, before: 28);
        Define("AcademicApprovalStart", before: 14, left: 5100, bold: true, keepNext: true);
        Define("AcademicSheetHeading", W.JustificationValues.Center, before: 14, after: 14, bold: true, keepNext: true);
        Define("AcademicStudentName", W.JustificationValues.Center, before: 7, keepNext: true);
        Define("AcademicFormItem", before: 14, keepNext: true);
        Define("AcademicFormText");
        Define("AcademicFormSignature", before: 56, keepNext: true);
        Define("AcademicAnnotationReference", before: 14, after: 21, left: 4800);
        Define("AcademicAnnotationText", W.JustificationValues.Both);
        foreach (var marker in new[] { "AcademicTitleEnd", "AcademicAssignmentPageEnd", "AcademicAssignmentEnd", "AcademicBodyStart" }) Define(marker);
        Set(styles.Elements<W.Style>().Single(s => s.StyleId == "AcademicAnnotationText").StyleParagraphProperties!,
            new W.Indentation { FirstLine = "397" });
        // Pandoc's TOC remains native. Start it on its own page after the annotation.
        var tocStyle = styles.Elements<W.Style>().FirstOrDefault(s => s.StyleId == "TOCHeading");
        if (tocStyle is not null) Set(tocStyle.StyleParagraphProperties ??= new W.StyleParagraphProperties(), new W.PageBreakBefore());

        var fields = body.Descendants<W.FieldCode>().Select(f => f.Text.Trim()).ToArray();
        var statistics = new AnnotationStatistics(
            fields.Count(f => f.StartsWith("SEQ Рисунок", StringComparison.Ordinal)),
            fields.Count(f => f.StartsWith("SEQ Таблица", StringComparison.Ordinal)),
            paragraphs.Count(p => NamedStyle(p).Equals("Bibliography", StringComparison.OrdinalIgnoreCase)),
            paragraphs.Count(p => NamedStyle(p).Equals("Heading1", StringComparison.OrdinalIgnoreCase) && p.InnerText.StartsWith("ПРИЛОЖЕНИЕ ", StringComparison.Ordinal)));
        var part = package.CustomFilePropertiesPart ?? package.AddCustomFilePropertiesPart();
        var properties = part.Properties ??= new C.Properties();
        foreach (var (name, value) in new[] { ("Figures", statistics.Figures), ("Tables", statistics.Tables),
            ("Sources", statistics.Sources), ("Appendices", statistics.Appendices) })
        {
            var propertyName = "Academic" + name;
            foreach (var old in properties.Elements<C.CustomDocumentProperty>().Where(p => p.Name == propertyName).ToArray()) old.Remove();
            var propertyId = properties.Elements<C.CustomDocumentProperty>().Select(p => p.PropertyId!.Value).DefaultIfEmpty(1).Max() + 1;
            properties.Append(new C.CustomDocumentProperty(new V.VTInt32(value.ToString()))
            { FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}", PropertyId = propertyId, Name = propertyName });
        }
        properties.Save();
        styles.Save();
        main.Document.Save();
        return statistics;
    }
}
