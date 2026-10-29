using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using AcademicDocx.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AcademicDocx.Tests;

public sealed class ReferenceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "academic-tests-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(AppContext.BaseDirectory, "reference.docx");
    public ReferenceTests() => Directory.CreateDirectory(folder);

    [Fact]
    public void StylesKeepNativeHeadingNumbersAndPageFields()
    {
        var output = Path.Combine(folder, "styled.docx");
        ReferenceStyles.Apply(Source, output);
        Assert.Empty(DocxInspection.Validate(output).Errors);
        using var document = WordprocessingDocument.Open(output, false);
        var main = document.MainDocumentPart!;
        var styles = main.StyleDefinitionsPart!.Styles!;
        for (var level = 1; level <= 4; level++)
        {
            var style = styles.Elements<W.Style>().Single(s => s.StyleId == $"Heading{level}");
            Assert.Equal("28", style.StyleRunProperties!.GetFirstChild<W.FontSize>()!.Val!.Value);
            Assert.Equal("397", style.StyleParagraphProperties!.Indentation!.FirstLine!.Value);
            Assert.Equal(level - 1, style.StyleParagraphProperties.OutlineLevel!.Val!.Value);
            var properties = style.StyleParagraphProperties.NumberingProperties!;
            var instance = main.NumberingDefinitionsPart!.Numbering!.Elements<W.NumberingInstance>()
                .Single(num => num.NumberID == properties.NumberingId!.Val);
            var abstractNum = main.NumberingDefinitionsPart.Numbering.Elements<W.AbstractNum>()
                .Single(num => num.AbstractNumberId == instance.AbstractNumId!.Val);
            Assert.Equal(string.Join('.', Enumerable.Range(1, level).Select(i => $"%{i}")),
                abstractNum.Elements<W.Level>().Single(l => l.LevelIndex?.Value == level - 1).LevelText!.Val!.Value);
        }
        Assert.Contains(main.FooterParts, part => part.Footer.Descendants<W.SimpleField>().Any(f => f.Instruction == "PAGE"));
        Assert.Equal(1474, main.Document.Descendants<W.SectionProperties>().First().GetFirstChild<W.PageMargin>()!.Bottom!.Value);
        Assert.Equal("340", styles.Elements<W.Style>().Single(s => s.StyleId == "Formula")
            .StyleParagraphProperties!.SpacingBetweenLines!.Before!.Value);
    }

    [Fact]
    public void ReapplyingStylesDoesNotDuplicateOrChangeXml()
    {
        var output = Path.Combine(folder, "repeat.docx");
        ReferenceStyles.Apply(Source, output);
        var first = RelevantXml(output);
        ReferenceStyles.Apply(output, output);
        var second = RelevantXml(output);
        Assert.Equal(first.Keys, second.Keys);
        foreach (var name in first.Keys) Assert.True(XNode.DeepEquals(first[name], second[name]), name);
    }

    [Fact]
    public void BottomMarginVariantKeepsTheSameStylesAndNumbering()
    {
        var standard = Path.Combine(folder, "standard.docx");
        var variant = Path.Combine(folder, "variant.docx");
        ReferenceStyles.Apply(Source, standard);
        ReferenceStyles.Apply(Source, variant, 20);
        var first = RelevantXml(standard);
        var second = RelevantXml(variant);
        Assert.True(XNode.DeepEquals(first["word/styles.xml"], second["word/styles.xml"]));
        Assert.True(XNode.DeepEquals(first["word/numbering.xml"], second["word/numbering.xml"]));
        using var doc = WordprocessingDocument.Open(variant, false);
        Assert.Equal(1134, doc.MainDocumentPart!.Document.Descendants<W.SectionProperties>().First().GetFirstChild<W.PageMargin>()!.Bottom!.Value);
    }

    [Fact]
    public void InvalidMarginLeavesExistingOutputUntouched()
    {
        var output = Path.Combine(folder, "existing.docx");
        File.Copy(Source, output);
        var before = SHA256.HashData(File.ReadAllBytes(output));
        Assert.Throws<ArgumentException>(() => ReferenceStyles.Apply(Source, output, 99));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(output)));
    }

    [Fact]
    public void UnchangedStylesMatchTheRetiredPythonEditor()
    {
        var output = Path.Combine(folder, "parity.docx");
        // Migration parity uses the input captured before later listing-style
        // changes. Those source styles are intentionally preserved by Apply.
        ReferenceStyles.Apply(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-migration-input.docx"), output);
        var expected = RelevantXml(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-legacy.docx"));
        var actual = RelevantXml(output);
        // Later user requirements intentionally change these four styles.
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var xml in new[] { expected["word/styles.xml"], actual["word/styles.xml"] })
            xml.Root!.Elements(word + "style").Where(style =>
                new[] { "Formula", "Table", "Hyperlink", "FollowedHyperlink" }.Contains(
                    (string?)style.Element(word + "name")?.Attribute(word + "val"), StringComparer.OrdinalIgnoreCase)).Remove();
        foreach (var name in expected.Keys)
            Assert.Equal(Normalize(expected[name].Root!), Normalize(actual[name].Root!));
    }

    [Fact]
    public void AcademicStylesCoverMarkdownTablesLinksAndCenteredObjects()
    {
        var output = Path.Combine(folder, "formatting.docx");
        ReferenceStyles.Apply(Source, output);
        Assert.Empty(DocxInspection.Validate(output).Errors);
        using var doc = WordprocessingDocument.Open(output, false);
        var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<W.Style>().ToArray();
        foreach (var name in new[] { "Hyperlink", "FollowedHyperlink" })
        {
            var properties = styles.Single(s => string.Equals(s.StyleName?.Val, name, StringComparison.OrdinalIgnoreCase)).StyleRunProperties!;
            var color = properties.GetFirstChild<W.Color>()!;
            Assert.Equal("000000", color.Val!.Value);
            Assert.Null(color.ThemeColor);
            Assert.Null(properties.GetFirstChild<W.FontSize>());
            Assert.Null(properties.GetFirstChild<W.FontSizeComplexScript>());
        }
        var table = styles.Single(s => s.StyleId == "Table");
        Assert.Equal("22", table.StyleRunProperties!.GetFirstChild<W.FontSize>()!.Val!.Value);
        var borders = table.StyleTableProperties!.TableBorders!;
        Assert.Equal(6, borders.ChildElements.Count);
        Assert.All(borders.ChildElements, border => Assert.Equal("single", border.GetAttribute("val", border.NamespaceUri).Value));
        Assert.Empty(table.Elements<W.TableStyleProperties>().SelectMany(s => s.Descendants<W.TableCellBorders>()));
        foreach (var id in new[] { "Formula", "ImageCaption" })
            Assert.Equal(W.JustificationValues.Center, styles.Single(s => s.StyleId == id).StyleParagraphProperties!.Justification!.Val!.Value);
    }

    private static string Normalize(XElement element)
    {
        var booleanElements = new[] { "b", "i", "caps", "keepNext", "keepLines", "pageBreakBefore", "widowControl", "suppressAutoHyphens" };
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        if (booleanElements.Contains(element.Name.LocalName) && attributes.All(a => a.Name.LocalName != "val"))
            attributes.Add(new XAttribute(element.Name.Namespace + "val", "true"));
        var attrs = attributes.OrderBy(a => a.Name.ToString()).Select(a =>
        {
            var value = a.Value;
            if (a.Name.LocalName == "val" && booleanElements.Contains(element.Name.LocalName))
                value = value is "0" or "false" or "off" ? "false" : "true";
            return a.Name + "=" + value;
        });
        return element.Name + "[" + string.Join(';', attrs) + "](" + string.Concat(element.Nodes().Select(node =>
            node is XElement child ? Normalize(child) : node.ToString())) + ")";
    }

    [Fact]
    public void SchemaValidationFindsDuplicateParagraphProperties()
    {
        var path = Path.Combine(folder, "invalid.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new W.Document(new W.Body(
                new W.Paragraph(new W.ParagraphProperties(), new W.ParagraphProperties(), new W.Run(new W.Text("invalid")))));
        }
        Assert.NotEmpty(DocxInspection.Validate(path).Errors);
    }

    [Fact]
    public void FailedStyleValidationPreservesExistingReference()
    {
        var input = Path.Combine(folder, "invalid.docx");
        using (var doc = WordprocessingDocument.Create(input, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("text")), new W.ParagraphProperties())));
        }
        var output = Path.Combine(folder, "existing.docx");
        File.Copy(Source, output);
        var before = SHA256.HashData(File.ReadAllBytes(output));
        Assert.Throws<InvalidDataException>(() => ReferenceStyles.Apply(input, output));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(output)));
    }

    private static SortedDictionary<string, XDocument> RelevantXml(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return new(zip.Entries.Where(entry => entry.FullName is "word/styles.xml" or "word/numbering.xml" or "word/document.xml" ||
            entry.FullName.StartsWith("word/footer", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .ToDictionary(entry => entry.FullName, entry => { using var stream = entry.Open(); return XDocument.Load(stream); }));
    }

    public void Dispose() => Directory.Delete(folder, true);
}
