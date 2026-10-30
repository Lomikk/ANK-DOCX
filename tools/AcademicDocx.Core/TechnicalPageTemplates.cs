using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using Wd = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Core;

/// <summary>Extracts the supplied Word drawings and calibrates their grid to STO 04 forms 2/2a.</summary>
public static class TechnicalPageTemplates
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace WPG = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
    private static readonly XNamespace WPS = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string SourceHash = "E367ECB5705CA408E18514015B4CB89DE9C7BAAEFEBCCF8F128E23F36272DEAA";
    private static string N(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

    public static void Prepare(string source, string destination)
    {
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != SourceHash)
            throw new InvalidDataException("This calibration is for the audited teacher template; source SHA-256 differs");
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(source);
        foreach (var (entry, name) in new[] { ("word/header2.xml", "title"), ("word/header4.xml", "first"), ("word/footer4.xml", "following") })
        {
            using var input = zip.GetEntry(entry)!.Open();
            var document = XDocument.Load(input);
            // Word 2010+ DrawingML is the supported native representation.
            // Drop the fallback so every slot has one authoritative value.
            foreach (var alternate in document.Descendants(MC + "AlternateContent").ToArray())
                alternate.ReplaceWith(alternate.Element(MC + "Choice")!.Elements().ToArray());
            var root = document.Root!;
            // The source footer also contains a loose "Лист" paragraph outside
            // its drawing. Only the calibrated page drawing belongs in the header.
            foreach (var text in root.Descendants(W + "t").Where(t => !t.Ancestors(W + "txbxContent").Any()).ToArray())
                text.Remove();
            root.Name = W + "hdr"; // Both frame variants are installed in headers.
            root.Attribute(MC + "Ignorable")?.Remove();
            var anchor = root.Descendants(WP + "anchor").Single();
            anchor.Element(WP + "positionH")!.ReplaceWith(new XElement(WP + "positionH", new XAttribute("relativeFrom", "page"), new XElement(WP + "posOffset", 20 * 36000)));
            anchor.Element(WP + "positionV")!.ReplaceWith(new XElement(WP + "positionV", new XAttribute("relativeFrom", "page"), new XElement(WP + "posOffset", 5 * 36000)));
            anchor.Element(WP + "extent")!.SetAttributeValue("cx", 185 * 36000);
            anchor.Element(WP + "extent")!.SetAttributeValue("cy", 287 * 36000);
            anchor.SetAttributeValue("behindDoc", "1");
            var group = root.Descendants(WPG + "wgp").SingleOrDefault();
            if (group is null)
            {
                var transform = root.Descendants(WPS + "spPr").First().Element(A + "xfrm")!;
                transform.Element(A + "off")!.SetAttributeValue("x", 0);
                transform.Element(A + "off")!.SetAttributeValue("y", 0);
                transform.Element(A + "ext")!.SetAttributeValue("cx", 185 * 36000);
                transform.Element(A + "ext")!.SetAttributeValue("cy", 287 * 36000);
            }
            else
            {
                var shapes = group.Elements().ToArray();
                var full = name == "first";
                double[] xs = full ? [1134, 1649, 2268, 3686, 4536, 5103, 8505, 9356, 10206, 11510] : [0, 1093, 2186, 4919, 6557, 7650, 18905, 20000];
                double[] xt = full ? [0, 7, 17, 40, 55, 65, 135, 150, 167, 185] : [0, 7, 17, 40, 55, 65, 175, 185];
                double[] ys = full ? [397, 14175, 14458, 14743, 15025, 15306, 15591, 15876, 16159, 16443] : [0, 18941, 19293, 19646, 20000];
                double[] yt = full ? [0, 247, 252, 257, 262, 267, 272, 277, 282, 287] : [0, 272, 277, 282, 287];
                double Map(double value, double[] from, double[] to)
                {
                    var index = Math.Clamp(Array.FindLastIndex(from, p => p <= value), 0, from.Length - 2);
                    return (to[index] + (value - from[index]) / (from[index + 1] - from[index]) * (to[index + 1] - to[index])) * 36000;
                }
                foreach (var child in shapes.Skip(2))
                {
                    var transform = (child.Element(WPS + "spPr") ?? child.Element(WPG + "grpSpPr"))!.Element(A + "xfrm")!;
                    var offset = transform.Element(A + "off")!;
                    var extent = transform.Element(A + "ext")!;
                    var x = (double)offset.Attribute("x")!; var y = (double)offset.Attribute("y")!;
                    var width = (double)extent.Attribute("cx")!; var height = (double)extent.Attribute("cy")!;
                    offset.SetAttributeValue("x", N(Map(x, xs, xt))); offset.SetAttributeValue("y", N(Map(y, ys, yt)));
                    extent.SetAttributeValue("cx", N(Map(x + width, xs, xt) - Map(x, xs, xt)));
                    extent.SetAttributeValue("cy", N(Map(y + height, ys, yt) - Map(y, ys, yt)));
                    if (child.Name == WPS + "wsp" && !child.Descendants(W + "txbxContent").Any())
                    {
                        double Snap(double emu, double[] grid)
                        {
                            var closest = grid.MinBy(mm => Math.Abs(mm * 36000 - emu));
                            return Math.Abs(closest * 36000 - emu) < 0.5 * 36000 ? closest * 36000 : emu;
                        }
                        var nx = Snap((double)offset.Attribute("x")!, xt); var ny = Snap((double)offset.Attribute("y")!, yt);
                        var ex = Snap((double)offset.Attribute("x")! + (double)extent.Attribute("cx")!, xt);
                        var ey = Snap((double)offset.Attribute("y")! + (double)extent.Attribute("cy")!, yt);
                        offset.SetAttributeValue("x", N(nx)); offset.SetAttributeValue("y", N(ny));
                        extent.SetAttributeValue("cx", N(Math.Max(0, ex - nx))); extent.SetAttributeValue("cy", N(Math.Max(0, ey - ny)));
                    }
                }
                var outer = group.Element(WPG + "grpSpPr")!.Element(A + "xfrm")!;
                foreach (var node in new[] { "off", "chOff" })
                {
                    outer.Element(A + node)!.SetAttributeValue("x", 0); outer.Element(A + node)!.SetAttributeValue("y", 0);
                }
                foreach (var node in new[] { "ext", "chExt" })
                {
                    outer.Element(A + node)!.SetAttributeValue("cx", 185 * 36000); outer.Element(A + node)!.SetAttributeValue("cy", 287 * 36000);
                }
                void Slot(int index, string tag, int size = 18) => SetText(shapes[index], tag, null, size);
                if (full)
                {
                    // Form 2 has four signatory rows. The teacher's added reviewer
                    // row is removed; change headings and the first two roles move down.
                    foreach (var index in Enumerable.Range(12, 5).Concat([25, 26]))
                    {
                        var tr = (shapes[index].Element(WPS + "spPr") ?? shapes[index].Element(WPG + "grpSpPr"))!.Element(A + "xfrm")!;
                        var off = tr.Element(A + "off")!;
                        off.SetAttributeValue("y", (long)off.Attribute("y")! + 5 * 36000);
                    }
                    var firstColumn = shapes[3].Element(WPS + "spPr")!.Element(A + "xfrm")!;
                    firstColumn.Element(A + "ext")!.SetAttributeValue("cy", 20 * 36000);
                    shapes[27].Remove();
                    SetText(shapes[13], null, "Лист");
                    Slot(18, "PAGE", 20); Slot(19, "document-code", 28);
                    Slot(31, "title", 22); Slot(37, "NUMPAGES", 20); Slot(40, "organization");
                    foreach (var (index, tag) in new[] { (25, "author"), (26, "supervisor"), (28, "normal-controller"), (29, "approver") })
                    {
                        var rowShapes = shapes[index].Elements(WPS + "wsp").ToArray();
                        var row = tag == "author" ? 267 : tag == "supervisor" ? 272 : tag == "normal-controller" ? 277 : 282;
                        var label = tag == "author" ? "Разраб." : tag == "supervisor" ? "Пров." : tag == "normal-controller" ? "Н.контр." : "Утв.";
                        // Flatten the teacher's nested name groups. Their local
                        // 20,000-unit text frames otherwise scale the text insets.
                        for (var column = 0; column < 2; column++)
                        {
                            var shape = rowShapes[column]; shape.Remove();
                            var transform = shape.Element(WPS + "spPr")!.Element(A + "xfrm")!;
                            transform.Element(A + "off")!.SetAttributeValue("x", (column == 0 ? 0 : 17) * 36000);
                            transform.Element(A + "off")!.SetAttributeValue("y", row * 36000);
                            transform.Element(A + "ext")!.SetAttributeValue("cx", (column == 0 ? 17 : 23) * 36000);
                            transform.Element(A + "ext")!.SetAttributeValue("cy", 5 * 36000);
                            SetText(shape, column == 0 ? null : tag, column == 0 ? label : null);
                            shapes[index].AddBeforeSelf(shape);
                        }
                        shapes[index].Remove();
                    }
                }
                else
                {
                    SetText(shapes[18], null, "Лист"); Slot(19, "PAGE", 20); Slot(20, "document-code", 28);
                }
            }
            // Every text box is independent of the source document's private styles.
            foreach (var paragraph in root.Descendants(W + "p").Where(p => p.Ancestors(W + "txbxContent").Any()))
            {
                var semantic = paragraph.Descendants(W + "sdt").Any() || paragraph.Descendants(W + "fldSimple").Any();
                var size = semantic ? (string?)paragraph.Descendants(W + "sz").FirstOrDefault()?.Attribute(W + "val") ?? "18" : "14";
                var pp = new Wd.ParagraphProperties();
                pp.AddChild(new Wd.SpacingBetweenLines { Before = "0", After = "0", Line = N(int.Parse(size, CultureInfo.InvariantCulture) * 10.5), LineRule = Wd.LineSpacingRuleValues.Exact }, true);
                pp.AddChild(new Wd.Indentation { FirstLine = "0", Left = "0", Right = "0" }, true);
                pp.AddChild(new Wd.Justification { Val = semantic ? Wd.JustificationValues.Center : Wd.JustificationValues.Left }, true);
                pp.AddChild(new Wd.ParagraphMarkRunProperties(new Wd.FontSize { Val = size }), true);
                paragraph.Element(W + "pPr")?.Remove();
                paragraph.AddFirst(XElement.Parse(pp.OuterXml));
                foreach (var run in paragraph.Descendants(W + "r"))
                {
                    var properties = new Wd.RunProperties(new Wd.RunFonts { Ascii = "Arial", HighAnsi = "Arial" },
                        new Wd.Italic(), new Wd.Color { Val = "000000" }, new Wd.FontSize { Val = size });
                    run.Element(W + "rPr")?.Remove(); run.AddFirst(XElement.Parse(properties.OuterXml));
                }
                foreach (var rp in paragraph.Descendants(W + "rPr").ToArray())
                {
                    rp.Elements(W + "rStyle").Remove();
                    rp.Elements(W + "color").Remove();
                    OpenXmlCompositeElement typed = rp.Parent?.Name == W + "pPr"
                        ? new Wd.ParagraphMarkRunProperties(rp.ToString()) : new Wd.RunProperties(rp.ToString());
                    typed.AddChild(new Wd.Color { Val = "000000" }, true);
                    rp.ReplaceWith(XElement.Parse(typed.OuterXml));
                }
            }
            foreach (var properties in root.Descendants(WPS + "bodyPr"))
            {
                properties.SetAttributeValue("lIns", 7200); properties.SetAttributeValue("rIns", 7200);
                properties.SetAttributeValue("tIns", 0); properties.SetAttributeValue("bIns", 0);
                properties.SetAttributeValue("anchor", "ctr");
            }
            var id = name == "title" ? 500000 : name == "first" ? 510000 : 520000;
            foreach (var property in root.Descendants().Where(e => e.Name == WP + "docPr" || e.Name.LocalName == "cNvPr"))
                property.SetAttributeValue("id", id++);
            root.Save(Path.Combine(destination, name + ".xml"));
        }
    }

    private static void SetText(XElement shape, string? tag, string? literal, int size = 18)
    {
        var body = shape.Descendants(W + "txbxContent").Single();
        var properties = new XElement(W + "rPr",
            new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial"), new XAttribute(W + "hAnsi", "Arial")),
            new XElement(W + "i"), new XElement(W + "sz", new XAttribute(W + "val", size)),
            new XElement(W + "color", new XAttribute(W + "val", "000000")));
        var paragraph = new XElement(W + "p", new XElement(W + "pPr",
            new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0)),
            new XElement(W + "ind", new XAttribute(W + "firstLine", 0)),
            new XElement(W + "jc", new XAttribute(W + "val", tag is "PAGE" or "NUMPAGES" or "document-code" ? "center" : "left"))));
        if (tag is "PAGE" or "NUMPAGES")
            paragraph.Add(new XElement(W + "fldSimple", new XAttribute(W + "instr", tag), new XElement(W + "r", properties, new XElement(W + "t", "0"))));
        else if (tag is not null)
            paragraph.Add(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "AcademicStamp." + tag))),
                new XElement(W + "sdtContent", new XElement(W + "r", properties, new XElement(W + "t", "")))));
        else paragraph.Add(new XElement(W + "r", properties, new XElement(W + "t", literal ?? "")));
        body.ReplaceNodes(paragraph);
    }
}
