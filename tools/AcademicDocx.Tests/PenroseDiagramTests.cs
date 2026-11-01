using System.Xml;
using System.Xml.Linq;
using AcademicDocx.Core;

namespace AcademicDocx.Tests;

public sealed class PenroseDiagramTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>Правка пользователя</text></svg>";

    [Fact]
    public void DiscoveryUsesNestedTrioFilesAndStableEditedNames()
    {
        WithProject((project, d) =>
        {
            Assert.Equal("схема/diagram", d.Id);
            Assert.EndsWith(Path.Combine("схема", "diagram.edited.svg"), d.Edited);
            Assert.EndsWith(Path.Combine(".quarto", "penrose", "схема", "diagram.svg"), d.Output);
            File.WriteAllText(d.Edited, Svg);
            var found = PenroseDiagram.Find(project);
            Assert.Single(found);
            Assert.True(found[0].HasEdits);
        });
    }

    [Fact]
    public void ReplacingAndReturningToAutomaticKeepExactPreviousEdits()
    {
        WithProject((_, d) =>
        {
            var incoming = Path.Combine(Path.GetDirectoryName(d.Trio)!, "download.svg");
            File.WriteAllText(incoming, Svg);
            d.Apply(incoming);
            var original = File.ReadAllBytes(d.Edited);
            File.WriteAllText(incoming, Svg.Replace("Правка пользователя", "Новая правка"));
            d.Apply(incoming);
            var history = Path.Combine(Path.GetDirectoryName(d.Edited)!, ".history");
            Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(history))));
            var second = File.ReadAllBytes(d.Edited);
            d.UseAutomatic(() => { });
            Assert.False(d.HasEdits);
            Assert.Equal(2, Directory.GetFiles(history).Length);
            Assert.Contains(Directory.GetFiles(history), p => File.ReadAllBytes(p).SequenceEqual(second));
        });
    }

    [Fact]
    public void FailedGenerationKeepsTheSavedVersionAndDoesNotCreateHistory()
    {
        WithProject((_, d) =>
        {
            File.WriteAllText(d.Edited, Svg);
            Assert.Throws<InvalidOperationException>(() => d.UseAutomatic(() => throw new InvalidOperationException("Renderer failed")));
            Assert.Equal(Svg, File.ReadAllText(d.Edited));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(d.Edited)!, ".history")));
        });
    }

    [Theory]
    [InlineData("<html>not an image</html>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\">")]
    [InlineData("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///missing'>]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
    public void InvalidImportLeavesSavedEditAndHistoryUntouched(string invalid)
    {
        WithProject((_, d) =>
        {
            File.WriteAllText(d.Edited, Svg);
            var incoming = Path.Combine(Path.GetDirectoryName(d.Trio)!, "bad.svg");
            File.WriteAllText(incoming, invalid);
            Assert.ThrowsAny<Exception>(() => d.Apply(incoming));
            Assert.Equal(Svg, File.ReadAllText(d.Edited));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(d.Edited)!, ".history")));
        });
    }

    [Fact]
    public void EditorSvgCarriesSharedSourcesAndPreservesMetadataOfAnExportedEdit()
    {
        WithProject((project, d) =>
        {
            var notation = Path.Combine(project, "figures", "penrose", "notation");
            Directory.CreateDirectory(notation);
            File.WriteAllText(Path.Combine(notation, "chen.domain"), "type Entity");
            File.WriteAllText(Path.Combine(notation, "chen.style"), "canvas {\n width = 300\n height = 300\n}");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(d.Trio)!, "diagram.substance"), "Entity Сад");
            File.WriteAllText(d.Trio, """{"domain":"../notation/chen.domain","substance":"diagram.substance","style":["../notation/chen.style"],"variation":"known-seed"}""");
            var svg = Path.Combine(project, "editor.svg");
            File.WriteAllText(svg, Svg);
            d.AttachEditorSources(svg);
            var document = XDocument.Load(svg);
            var metadata = document.Root!.Elements().Single(e => e.Name.LocalName == "penrose");
            Assert.Equal("type Entity", metadata.Element("dsl")!.Value);
            Assert.Equal("Entity Сад", metadata.Element("sub")!.Value);
            Assert.Equal("known-seed", metadata.Element("variation")!.Value);
            metadata.Element("sty")!.Value = "User edited source";
            document.Save(svg);
            d.AttachEditorSources(svg);
            Assert.Contains("User edited source", File.ReadAllText(svg));
        });
    }

    private static void WithProject(Action<string, PenroseDiagram> test)
    {
        var project = Path.Combine(Path.GetTempPath(), "ANK Penrose " + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(project, "figures", "penrose", "схема");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "diagram.trio.json"), "{}");
        try { test(project, Assert.Single(PenroseDiagram.Find(project))); }
        finally { Directory.Delete(project, recursive: true); }
    }
}
