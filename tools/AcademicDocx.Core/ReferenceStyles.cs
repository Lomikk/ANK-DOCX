using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Core;

// University conventions from docs/REFERENCE_DOCX.md. This edits a Pandoc
// reference using the SDK; it does not implement a writer or normative engine.
public static class ReferenceStyles
{
    public const string TextColor = "000000";
    public const string TableFontSize = "22";
    private static string Twips(double mm) => Math.Round(mm * 1440 / 25.4).ToString(CultureInfo.InvariantCulture);

    private static T Set<T>(OpenXmlCompositeElement parent, T child) where T : OpenXmlElement
    {
        parent.RemoveAllChildren<T>();
        parent.AddChild(child, true);
        return child;
    }

    private static void Font(OpenXmlCompositeElement properties)
    {
        var fonts = properties.GetFirstChild<W.RunFonts>() ?? new W.RunFonts();
        fonts.AsciiTheme = null;
        fonts.HighAnsiTheme = null;
        fonts.EastAsiaTheme = null;
        fonts.ComplexScriptTheme = null;
        fonts.Ascii = fonts.HighAnsi = fonts.EastAsia = fonts.ComplexScript = "Times New Roman";
        Set(properties, fonts);
        var color = properties.GetFirstChild<W.Color>() ?? new W.Color();
        color.Val = TextColor;
        Set(properties, color);
        Set(properties, new W.FontSize { Val = "28" });
        Set(properties, new W.FontSizeComplexScript { Val = "28" });
        var language = properties.GetFirstChild<W.Languages>() ?? new W.Languages();
        language.Val = "ru-RU";
        Set(properties, language);
    }

    public static void Apply(string input, string output, int bottomMarginMm = 26)
    {
        if (bottomMarginMm is not (20 or 26)) throw new ArgumentException("Bottom margin must be 20 or 26 mm");
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var staged = Path.Combine(Path.GetDirectoryName(output)!, $".reference-{Guid.NewGuid():N}.docx");
        try
        {
            File.Copy(input, staged);
            ApplyInPlace(staged, bottomMarginMm);
            var validation = DocxInspection.Validate(staged);
            if (validation.ErrorCount > 0) throw new InvalidDataException($"Reference DOCX failed SDK validation: {validation.Errors[0].Description}");
            File.Move(staged, output, overwrite: true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private static void ApplyInPlace(string path, int bottomMarginMm)
    {
        using var package = WordprocessingDocument.Open(path, true);
        var main = package.MainDocumentPart ?? throw new InvalidDataException("Missing main document part");
        var stylesPart = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        var styles = stylesPart.Styles ??= new W.Styles();
        var normal = styles.Elements<W.Style>().FirstOrDefault(style => style.StyleId == "Normal");
        if (normal is null) styles.Append(new W.Style { Type = W.StyleValues.Paragraph, StyleId = "Normal", StyleName = new W.StyleName { Val = "Normal" } });

        W.Style Style(string name, W.JustificationValues? align = null, double indent = 7,
            int before = 0, int after = 0, bool bold = false, bool italic = false,
            bool caps = false, bool keepNext = false, bool keepLines = false,
            bool newPage = false, int? outline = null)
        {
            var id = name.Replace(" ", "", StringComparison.Ordinal);
            var style = styles.Elements<W.Style>().FirstOrDefault(s =>
                s.StyleId == id || string.Equals(s.StyleName?.Val, name, StringComparison.OrdinalIgnoreCase));
            if (style is null)
            {
                style = new W.Style { Type = W.StyleValues.Paragraph, StyleId = id, StyleName = new W.StyleName { Val = name } };
                styles.Append(style);
            }
            if (name != "Normal") Set(style, new W.BasedOn { Val = "Normal" });
            var rp = style.StyleRunProperties ??= new W.StyleRunProperties();
            Font(rp);
            Set(rp, new W.Bold { Val = bold });
            Set(rp, new W.Italic { Val = italic });
            Set(rp, new W.Caps { Val = caps });
            var pp = style.StyleParagraphProperties ??= new W.StyleParagraphProperties();
            Set(pp, new W.Justification { Val = align ?? W.JustificationValues.Both });
            Set(pp, new W.Indentation { FirstLine = Twips(indent), Left = "0", Right = "0" });
            Set(pp, new W.SpacingBetweenLines { Before = (before * 20).ToString(CultureInfo.InvariantCulture), After = (after * 20).ToString(CultureInfo.InvariantCulture), Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
            Set(pp, new W.WidowControl { Val = true });
            Set(pp, new W.KeepNext { Val = keepNext });
            Set(pp, new W.KeepLines { Val = keepLines });
            Set(pp, new W.PageBreakBefore { Val = newPage });
            if (outline.HasValue) Set(pp, new W.OutlineLevel { Val = outline.Value });
            if (style.LinkedStyle?.Val is { } linkedId)
            {
                var linked = styles.Elements<W.Style>().FirstOrDefault(s => s.StyleId == linkedId);
                if (linked is not null) Font(linked.StyleRunProperties ??= new W.StyleRunProperties());
            }
            return style;
        }

        foreach (var name in new[] { "Normal", "Body Text", "First Paragraph", "Bibliography" }) Style(name);
        Style("Compact", W.JustificationValues.Left, indent: 0);
        Style("Formula", W.JustificationValues.Center, indent: 0, before: 17, after: 17, keepLines: true);
        Style("Formula Definitions", W.JustificationValues.Left, indent: 0, keepLines: true);
        for (var level = 1; level <= 4; level++)
        {
            var heading = Style($"Heading {level}", W.JustificationValues.Left,
                before: level <= 2 ? 14 : 7, after: level == 1 ? 14 : 7,
                bold: level <= 2, italic: level >= 3, caps: level == 1,
                keepNext: true, keepLines: true, outline: level - 1);
            Set(heading, new W.NextParagraphStyle { Val = "BodyText" });
            Set(heading.StyleParagraphProperties!, new W.SuppressAutoHyphens { Val = true });
        }
        var unnumbered = Style("Unnumbered Heading", W.JustificationValues.Center, indent: 0,
            after: 14, bold: true, caps: true, keepNext: true, keepLines: true, newPage: true, outline: 0);
        Set(unnumbered, new W.NextParagraphStyle { Val = "BodyText" });
        Set(unnumbered.StyleParagraphProperties!, new W.SuppressAutoHyphens { Val = true });

        var numberingPart = main.NumberingDefinitionsPart ?? main.AddNewPart<NumberingDefinitionsPart>();
        var numbering = numberingPart.Numbering ??= new W.Numbering();
        // Fix the historical demonstration's interleaved abstractNum/num order.
        var abstracts = numbering.Elements<W.AbstractNum>().ToArray();
        foreach (var abstractNum in abstracts) abstractNum.Remove();
        var insertIndex = numbering.Elements<W.NumberingPictureBullet>().Count();
        foreach (var abstractNum in abstracts)
        {
            numbering.InsertAt(abstractNum, insertIndex++);
        }
        var headings = numbering.Elements<W.AbstractNum>().FirstOrDefault(num =>
            num.Descendants<W.ParagraphStyleIdInLevel>().FirstOrDefault()?.Val == "Heading1");
        W.NumberingInstance? instance;
        if (headings is null)
        {
            var abstractId = numbering.Elements<W.AbstractNum>().Select(num => num.AbstractNumberId!.Value).DefaultIfEmpty(0).Max() + 1;
            var numId = numbering.Elements<W.NumberingInstance>().Select(num => num.NumberID!.Value).DefaultIfEmpty(0).Max() + 1;
            headings = new W.AbstractNum { AbstractNumberId = abstractId };
            numbering.InsertAt(headings, numbering.Elements<W.NumberingPictureBullet>().Count() + numbering.Elements<W.AbstractNum>().Count());
            instance = new W.NumberingInstance(new W.AbstractNumId { Val = abstractId }) { NumberID = numId };
            numbering.InsertBefore(instance, numbering.ChildElements.FirstOrDefault(child => child.LocalName == "numIdMacAtCleanup"));
        }
        else instance = numbering.Elements<W.NumberingInstance>().First(num => num.AbstractNumId?.Val == headings.AbstractNumberId);
        headings.RemoveAllChildren();
        headings.AddChild(new W.MultiLevelType { Val = W.MultiLevelValues.Multilevel }, true);
        for (var level = 0; level < 4; level++)
        {
            var rp = new W.NumberingSymbolRunProperties();
            Font(rp);
            var definition = new W.Level(
                new W.StartNumberingValue { Val = 1 },
                new W.NumberingFormat { Val = W.NumberFormatValues.Decimal },
                new W.ParagraphStyleIdInLevel { Val = $"Heading{level + 1}" },
                new W.LevelSuffix { Val = W.LevelSuffixValues.Space },
                new W.LevelText { Val = string.Join('.', Enumerable.Range(1, level + 1).Select(i => $"%{i}")) },
                new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                new W.PreviousParagraphProperties(new W.Indentation { Left = "0", FirstLine = "397" }), rp) { LevelIndex = level };
            headings.Append(definition);
            var heading = styles.Elements<W.Style>().First(s => s.StyleId == $"Heading{level + 1}");
            Set(heading.StyleParagraphProperties!, new W.NumberingProperties(new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = instance.NumberID!.Value }));
        }
        foreach (var name in new[] { "Caption", "Image Caption", "Table Caption", "Figure", "Captioned Figure" })
            Style(name, name == "Table Caption" ? W.JustificationValues.Left : W.JustificationValues.Center,
                indent: 0, keepNext: name is "Table Caption" or "Figure" or "Captioned Figure", keepLines: true);
        // Character styles inherit the surrounding size, including 11 pt tables.
        foreach (var name in new[] { "Hyperlink", "FollowedHyperlink" })
        {
            var link = styles.Elements<W.Style>().FirstOrDefault(s =>
                s.StyleId == name || string.Equals(s.StyleName?.Val, name, StringComparison.OrdinalIgnoreCase));
            if (link is null)
            {
                link = new W.Style { Type = W.StyleValues.Character, StyleId = name,
                    StyleName = new W.StyleName { Val = name } };
                styles.Append(link);
            }
            var properties = link.StyleRunProperties ??= new W.StyleRunProperties();
            properties.RemoveAllChildren<W.FontSize>();
            properties.RemoveAllChildren<W.FontSizeComplexScript>();
            Set(properties, new W.Color { Val = TextColor });
        }
        // Pandoc Markdown tables use this public reference-doc table style.
        // Flextable retains its explicit cell formatting on the same foundation.
        var tableStyle = styles.Elements<W.Style>().FirstOrDefault(s =>
            s.Type?.Value == W.StyleValues.Table &&
            (s.StyleId == "Table" || string.Equals(s.StyleName?.Val, "Table", StringComparison.OrdinalIgnoreCase)));
        if (tableStyle is null)
        {
            tableStyle = new W.Style { Type = W.StyleValues.Table, StyleId = "Table",
                StyleName = new W.StyleName { Val = "Table" } };
            styles.Append(tableStyle);
        }
        var tableRun = tableStyle.StyleRunProperties ??= new W.StyleRunProperties();
        Font(tableRun);
        Set(tableRun, new W.FontSize { Val = TableFontSize });
        Set(tableRun, new W.FontSizeComplexScript { Val = TableFontSize });
        var tableProperties = tableStyle.StyleTableProperties ??= new W.StyleTableProperties();
        Set(tableProperties, new W.TableBorders(
            new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = TextColor },
            new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = TextColor },
            new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = TextColor },
            new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = TextColor },
            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = TextColor },
            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = TextColor }));
        foreach (var condition in tableStyle.Elements<W.TableStyleProperties>())
            condition.TableStyleConditionalFormattingTableCellProperties?.RemoveAllChildren<W.TableCellBorders>();
        Style("TOC Heading", W.JustificationValues.Center, indent: 0, bold: true, caps: true, after: 14, keepNext: true, keepLines: true);
        var defaults = styles.DocDefaults ??= new W.DocDefaults();
        var runDefaults = defaults.RunPropertiesDefault ??= new W.RunPropertiesDefault();
        Font(runDefaults.RunPropertiesBaseStyle ??= new W.RunPropertiesBaseStyle());
        var paragraphDefaults = defaults.ParagraphPropertiesDefault ??= new W.ParagraphPropertiesDefault();
        var ppDefaults = paragraphDefaults.ParagraphPropertiesBaseStyle ??= new W.ParagraphPropertiesBaseStyle();
        Set(ppDefaults, new W.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
        Set(ppDefaults, new W.Indentation { FirstLine = "397" });
        foreach (var section in main.Document.Descendants<W.SectionProperties>())
        {
            var pageSize = section.GetFirstChild<W.PageSize>() ?? new W.PageSize();
            pageSize.Width = uint.Parse(Twips(210), CultureInfo.InvariantCulture);
            pageSize.Height = uint.Parse(Twips(297), CultureInfo.InvariantCulture);
            Set(section, pageSize);
            var margins = section.GetFirstChild<W.PageMargin>() ?? new W.PageMargin();
            margins.Top = int.Parse(Twips(20), CultureInfo.InvariantCulture);
            margins.Bottom = int.Parse(Twips(bottomMarginMm), CultureInfo.InvariantCulture);
            margins.Left = uint.Parse(Twips(25), CultureInfo.InvariantCulture);
            margins.Right = uint.Parse(Twips(10), CultureInfo.InvariantCulture);
            margins.Footer = uint.Parse(Twips(20), CultureInfo.InvariantCulture);
            Set(section, margins);
            section.RemoveAllChildren<W.TitlePage>();
            var footerRef = section.Elements<W.FooterReference>().FirstOrDefault(f => f.Type?.Value == W.HeaderFooterValues.Default);
            FooterPart footer;
            if (footerRef?.Id is not null) footer = (FooterPart)main.GetPartById(footerRef.Id!);
            else
            {
                footer = main.AddNewPart<FooterPart>();
                section.AddChild(new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) }, true);
            }
            footer.Footer ??= new W.Footer();
            var p = footer.Footer.Elements<W.Paragraph>().FirstOrDefault();
            if (p is null) { p = new W.Paragraph(); footer.Footer.Append(p); }
            var footerProperties = p.ParagraphProperties ?? new W.ParagraphProperties();
            p.RemoveAllChildren();
            p.ParagraphProperties = footerProperties;
            Set(footerProperties, new W.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
            Set(footerProperties, new W.Indentation { FirstLine = "0", Left = Twips(-25), Right = Twips(-10) });
            Set(footerProperties, new W.Justification { Val = W.JustificationValues.Center });
            p.Append(new W.SimpleField(new W.Run(new W.Text("1"))) { Instruction = "PAGE" });
        }
        main.Document.Save();
        styles.Save();
        numbering.Save();
    }
}
