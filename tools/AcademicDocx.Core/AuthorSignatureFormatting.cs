using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Core;

/// <summary>Overlays Pandoc's signature picture on the retained signature line.</summary>
public static class AuthorSignatureFormatting
{
    public static void Apply(string path)
    {
        using var package = WordprocessingDocument.Open(path, true);
        foreach (var inline in package.MainDocumentPart!.Document.Descendants<DW.Inline>()
            .Where(i => i.DocProperties?.Description == "Подпись автора").ToArray())
        {
            var paragraph = inline.Ancestors<W.Paragraph>().First();
            if (!paragraph.InnerText.Contains("____________", StringComparison.Ordinal))
                throw new InvalidDataException("Author signature has no signature line");
            var height = inline.Extent!.Cy!.Value;
            // The shared forms use 14 pt text. The line crosses the lower part
            // of the signature's main strokes; descenders may overlap nearby
            // text. Floating without wrapping preserves the original form.
            var anchor = new DW.Anchor(
                new DW.SimplePosition { X = 0, Y = 0 },
                new DW.HorizontalPosition(new DW.PositionOffset("0")) { RelativeFrom = DW.HorizontalRelativePositionValues.Character },
                new DW.VerticalPosition(new DW.PositionOffset((14 * 12700L - (long)(height * 0.6)).ToString(CultureInfo.InvariantCulture))) { RelativeFrom = DW.VerticalRelativePositionValues.Line },
                inline.Extent.CloneNode(true),
                inline.EffectExtent?.CloneNode(true) ?? new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                new DW.WrapNone(), inline.DocProperties!.CloneNode(true),
                inline.NonVisualGraphicFrameDrawingProperties?.CloneNode(true) ?? new DW.NonVisualGraphicFrameDrawingProperties(),
                inline.Graphic!.CloneNode(true))
            {
                DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0,
                SimplePos = false, RelativeHeight = 1, BehindDoc = false, Locked = false,
                LayoutInCell = true, AllowOverlap = true
            };
            inline.Parent!.ReplaceChild(anchor, inline);
        }
        package.MainDocumentPart.Document.Save();
    }
}
