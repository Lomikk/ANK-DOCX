using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Core;

public sealed record AppendixInfo(string Id, string Letter, string Title, string Kind);

// Both QMD and Book supply ordinary heading IDs. Word owns contents and pagination.
public static class AppendixFormatting
{
    internal static string BookmarkId(AppendixInfo appendix) => Regex.Replace(appendix.Id, "[^A-Za-z0-9_]", "_");
    public static W.Paragraph Heading(W.Body body, AppendixInfo appendix)
    {
        var normalized = BookmarkId(appendix);
        var bookmark = body.Descendants<W.BookmarkStart>().SingleOrDefault(b => b.Name == appendix.Id || b.Name == normalized)
            ?? throw new InvalidDataException("Missing appendix bookmark: " + appendix.Id);
        var heading = bookmark.Ancestors<W.Paragraph>().FirstOrDefault()
            ?? bookmark.ElementsAfter().OfType<W.Paragraph>().FirstOrDefault();
        if (heading?.Parent != body) throw new InvalidDataException("Appendix must begin with a body heading: " + appendix.Id);
        return heading;
    }

    private static W.Paragraph Paragraph(string style, string text) =>
        new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }), new W.Run(new W.Text(text)));

    public static void Apply(string path, PageLayoutOptions? options)
    {
        if (options?.Appendices is not { Count: > 0 } appendices) return;
        if (options.AppendixLayout is not ("basic" or "research")) throw new InvalidDataException("Unsupported appendix layout");
        var research = options.AppendixLayout == "research";
        using var package = WordprocessingDocument.Open(path, true);
        var main = package.MainDocumentPart!;
        var body = main.Document.Body!;
        var styles = main.StyleDefinitionsPart!.Styles!;
        // Regeneration owns only these generated paragraphs, never author text.
        foreach (var p in body.Elements<W.Paragraph>().Where(p =>
            p.ParagraphProperties?.ParagraphStyleId?.Val?.Value is "AcademicAppendixCollection" or "AcademicAppendixKind" or "AcademicAppendixTitle").ToArray()) p.Remove();
        if (research)
        {
            foreach (var id in new[] { "AcademicAppendixCollection", "AcademicAppendixLabel", "AcademicAppendixKind", "AcademicAppendixTitle", "AcademicAppendixBoundary" })
            {
                foreach (var old in styles.Elements<W.Style>().Where(s => s.StyleId == id).ToArray()) old.Remove();
                var pp = new W.StyleParagraphProperties(new W.KeepNext { Val = id != "AcademicAppendixBoundary" }, new W.KeepLines(),
                    new W.SpacingBetweenLines { Before = "0", After = id is "AcademicAppendixCollection" or "AcademicAppendixTitle" ? "280" : "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto },
                    new W.Indentation { Left = "0", Right = "0", FirstLine = "0" },
                    new W.Justification { Val = W.JustificationValues.Center }, new W.OutlineLevel { Val = 9 });
                styles.Append(new W.Style(new W.StyleName { Val = id }, new W.BasedOn { Val = "Normal" }, pp,
                    new W.StyleRunProperties(new W.Bold { Val = id is "AcademicAppendixCollection" or "AcademicAppendixLabel" },
                        new W.Caps { Val = false }, new W.Color { Val = "000000" }, new W.FontSize { Val = "28" }))
                    { StyleId = id, Type = W.StyleValues.Paragraph });
            }
        }
        for (var index = 0; index < appendices.Count; index++)
        {
            var appendix = appendices[index];
            var p = Heading(body, appendix);
            var pp = p.ParagraphProperties ?? new W.ParagraphProperties();
            foreach (var child in pp.ChildElements.Where(e => e is W.NumberingProperties or W.Justification or W.PageBreakBefore).ToArray()) child.Remove();
            pp.AddChild(new W.NumberingProperties(new W.NumberingId { Val = 0 }), true);
            pp.AddChild(new W.Justification { Val = W.JustificationValues.Center }, true);
            pp.AddChild(new W.PageBreakBefore(), true);
            foreach (var child in p.ChildElements.Where(e => e is not (W.ParagraphProperties or W.BookmarkStart or W.BookmarkEnd)).ToArray()) child.Remove();
            if (p.ParagraphProperties is null) p.PrependChild(pp);
            p.Append(new W.Run(new W.Text("ПРИЛОЖЕНИЕ " + appendix.Letter)));
            if (!research)
            {
                pp.ParagraphStyleId = new W.ParagraphStyleId { Val = "Heading1" };
                p.Append(new W.Run(new W.Break()), new W.Run(new W.RunProperties(new W.Caps { Val = false }), new W.Text(appendix.Title)));
                continue;
            }
            var kind = appendix.Kind switch
            {
                "mandatory" => "обязательное",
                "informational" => "информационное",
                _ => throw new InvalidDataException("Unsupported appendix kind: " + appendix.Kind)
            };
            pp.ParagraphStyleId = new W.ParagraphStyleId { Val = "AcademicAppendixLabel" };
            if (index == 0)
            {
                var collection = Paragraph("AcademicAppendixCollection", "ПРИЛОЖЕНИЯ");
                var normalized = BookmarkId(appendix);
                var anchor = p.ElementsBefore().OfType<W.BookmarkStart>().LastOrDefault(b => b.Name == appendix.Id || b.Name == normalized);
                if (anchor is not null) anchor.InsertBeforeSelf(collection); else p.InsertBeforeSelf(collection);
            }
            var kindParagraph = Paragraph("AcademicAppendixKind", "(" + kind + ")");
            p.InsertAfterSelf(kindParagraph);
            kindParagraph.InsertAfterSelf(Paragraph("AcademicAppendixTitle", appendix.Title));
            // Native TC gives the contents an entry without the status line.
            var entry = ("ПРИЛОЖЕНИЕ " + appendix.Letter + ". " + appendix.Title).Replace("\\", "\\\\").Replace("\"", "\\\"");
            p.Append(new W.Run(new W.RunProperties(new W.Vanish()), new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
                new W.Run(new W.RunProperties(new W.Vanish()), new W.FieldCode($" TC \"{entry}\" \\f A \\l 1 ") { Space = SpaceProcessingModeValues.Preserve }),
                new W.Run(new W.RunProperties(new W.Vanish()), new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
        }
        if (research)
            foreach (var toc in body.Descendants<W.FieldCode>().Where(f => f.Text.TrimStart().StartsWith("TOC ", StringComparison.Ordinal)))
                if (!toc.Text.Contains("\\f A", StringComparison.Ordinal)) toc.Text += " \\f A";
        styles.Save();
        main.Document.Save();
    }
}
