using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;

namespace AcademicDocx.Core;

/// <summary>Normalizes direct formatting that overrides the academic reference.</summary>
public static class DocumentFormatting
{
    private static void Set<T>(OpenXmlCompositeElement parent, T value) where T : OpenXmlElement
    {
        parent.RemoveAllChildren<T>();
        parent.AddChild(value, true);
    }

    private static void TableSize(OpenXmlCompositeElement properties)
    {
        Set(properties, new W.FontSize { Val = ReferenceStyles.TableFontSize });
        Set(properties, new W.FontSizeComplexScript { Val = ReferenceStyles.TableFontSize });
    }

    public static void Apply(string path)
    {
        using var package = WordprocessingDocument.Open(path, true);
        var body = package.MainDocumentPart?.Document.Body ?? throw new InvalidDataException("DOCX has no body");
        foreach (var table in body.Descendants<W.Table>())
        {
            foreach (var paragraph in table.Descendants<W.Paragraph>())
            {
                var properties = paragraph.ParagraphProperties ??= new W.ParagraphProperties();
                var mark = properties.GetFirstChild<W.ParagraphMarkRunProperties>();
                if (mark is null)
                {
                    mark = new W.ParagraphMarkRunProperties();
                    properties.AddChild(mark, true);
                }
                TableSize(mark);
            }
            foreach (var run in table.Descendants<W.Run>())
                TableSize(run.RunProperties ??= new W.RunProperties());
            // Math is a sibling of ordinary Word runs. Flextable fontsize and
            // as_equation(props=...) do not supply its effective Word run size.
            foreach (var run in table.Descendants<M.Run>())
            {
                var properties = run.GetFirstChild<W.RunProperties>();
                if (properties is null)
                {
                    properties = new W.RunProperties();
                    run.AddChild(properties, true);
                }
                TableSize(properties);
            }
            foreach (var control in table.Descendants<M.ControlProperties>())
            {
                var properties = control.GetFirstChild<W.RunProperties>();
                if (properties is null)
                {
                    properties = new W.RunProperties();
                    control.AddChild(properties, true);
                }
                TableSize(properties);
            }
        }

        foreach (var paragraph in body.Descendants<W.Paragraph>().Where(p => !p.Ancestors<W.Table>().Any()))
        {
            var fields = paragraph.Descendants<W.FieldCode>().Select(f => f.Text.Trim()).ToArray();
            if (fields.Any(f => f.StartsWith("SEQ Рисунок ", StringComparison.Ordinal) ||
                f.StartsWith("SEQ РисунокAppendix", StringComparison.Ordinal)))
            {
                // Quarto forces a direct left alignment even when Image Caption
                // is centered in the reference. Let that reference style apply.
                // Quarto can emit more than one pPr block before Word repairs
                // the package; the later block otherwise restores "left".
                foreach (var properties in paragraph.Elements<W.ParagraphProperties>())
                    properties.RemoveAllChildren<W.Justification>();
            }
            if (fields.Any(f => f.StartsWith("SEQ Формула ", StringComparison.Ordinal)))
                CenterNumberedEquation(body, paragraph);
            else
            {
                foreach (var math in paragraph.Elements<M.Paragraph>())
                {
                    var properties = math.GetFirstChild<M.ParagraphProperties>();
                    if (properties is null)
                    {
                        properties = new M.ParagraphProperties();
                        math.AddChild(properties, true);
                    }
                    Set(properties, new M.Justification { Val = M.JustificationValues.Center });
                }
            }
        }
        // Word requires a terminal paragraph after a table. An empty 14 pt
        // paragraph can create a page of its own when that table fills a page.
        // Compact only that structural paragraph, never author text.
        var last = body.ChildElements.LastOrDefault(e => e is not (W.SectionProperties or W.BookmarkStart or W.BookmarkEnd));
        if (last is W.Table finalTable)
        {
            last = new W.Paragraph();
            finalTable.InsertAfterSelf(last);
        }
        if (last is W.Paragraph terminal && terminal.PreviousSibling() is W.Table &&
            terminal.ChildElements.All(e => e is W.ParagraphProperties))
        {
            var properties = terminal.ParagraphProperties ??= new W.ParagraphProperties();
            Set(properties, new W.SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = W.LineSpacingRuleValues.Exact });
            var mark = properties.GetFirstChild<W.ParagraphMarkRunProperties>() ?? new W.ParagraphMarkRunProperties();
            Set(mark, new W.FontSize { Val = "2" });
            Set(mark, new W.FontSizeComplexScript { Val = "2" });
            Set(properties, mark);
        }
        package.MainDocumentPart!.Document.Save();
    }

    private static void CenterNumberedEquation(W.Body body, W.Paragraph paragraph)
    {
        // Pandoc emits display OMML here. Word otherwise normalizes it to
        // inline math when the paragraph also contains the tab and SEQ field.
        // Make that representation explicit before applying center/right tabs.
        foreach (var display in paragraph.Elements<M.Paragraph>().ToArray())
        {
            foreach (var expression in display.Elements<M.OfficeMath>().ToArray())
            {
                expression.Remove();
                display.InsertBeforeSelf(expression);
            }
            display.Remove();
        }
        var math = paragraph.Elements<M.OfficeMath>().FirstOrDefault();
        if (math is null) throw new InvalidDataException("Numbered equation has no inline OMML expression");
        var section = body.Descendants<W.SectionProperties>().LastOrDefault();
        var size = section?.GetFirstChild<W.PageSize>();
        var margins = section?.GetFirstChild<W.PageMargin>();
        var width = (int)(size?.Width?.Value ?? 0) - (int)(margins?.Left?.Value ?? 0) - (int)(margins?.Right?.Value ?? 0);
        if (width <= 0) throw new InvalidDataException("Cannot determine the text width for equation alignment");
        var properties = paragraph.ParagraphProperties ??= new W.ParagraphProperties();
        Set(properties, new W.Justification { Val = W.JustificationValues.Left });
        Set(properties, new W.Indentation { FirstLine = "0", Left = "0", Right = "0" });
        Set(properties, new W.Tabs(
            new W.TabStop { Val = W.TabStopValues.Center, Position = width / 2 },
            new W.TabStop { Val = W.TabStopValues.Right, Position = width }));
        // A center tab places the expression about the text midpoint; the
        // existing second tab keeps its SEQ number at the right margin.
        if (math.PreviousSibling() is not W.Run leading || !leading.Elements<W.TabChar>().Any() || leading.InnerText.Length > 0)
            math.InsertBeforeSelf(new W.Run(new W.TabChar()));
    }
}
