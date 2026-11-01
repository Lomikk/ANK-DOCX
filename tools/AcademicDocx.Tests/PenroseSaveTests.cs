using System.IO.Compression;
using System.Text.Json;
using AcademicDocx.Core;

namespace AcademicDocx.Tests;

public sealed class PenroseSaveTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>Изменено</text></svg>";

    [Fact]
    public void SaveCreatesPrivateSourcesAndKeepsOtherDiagramsAndHistory()
    {
        WithDiagram((d, shared) =>
        {
            var before = File.ReadAllText(d.Trio);
            var first = new PenroseSource("type New", "New A", "canvas {}", "seed", Svg);
            d.SaveToWorkspace(first, (trio, svg) =>
            {
                Assert.True(File.Exists(trio));
                Assert.Contains("Изменено", File.ReadAllText(svg));
                Assert.Equal(before, File.ReadAllText(d.Trio));
            });
            Assert.Equal("type Original", File.ReadAllText(shared));
            using var descriptor = JsonDocument.Parse(File.ReadAllText(d.Trio));
            var domain = Path.Combine(Path.GetDirectoryName(d.Trio)!, descriptor.RootElement.GetProperty("domain").GetString()!);
            Assert.Equal(first.Domain, File.ReadAllText(domain));
            Assert.Contains("seed", File.ReadAllText(d.Edited));
            var history = Path.Combine(Path.GetDirectoryName(d.Trio)!, ".history");
            var saved = Assert.Single(Directory.GetFiles(history));
            using (var zip = ZipFile.OpenRead(saved))
            {
                using var reader = new StreamReader(zip.GetEntry("diagram.trio.json")!.Open());
                Assert.Equal(before, reader.ReadToEnd());
                Assert.Contains(zip.Entries, e => e.FullName.EndsWith("shared.domain"));
            }
            var oldSvg = File.ReadAllText(d.Edited);
            d.SaveToWorkspace(first with { Domain = "type Next" }, (_, _) => { });
            Assert.Equal(2, Directory.GetFiles(history).Length);
            Assert.Equal("type Next", File.ReadAllText(domain));
            Assert.Equal("type Original", File.ReadAllText(shared));
            Assert.NotEmpty(oldSvg);
            Assert.Single(PenroseDiagram.Find(d.Project));
        });
    }

    [Fact]
    public void FailedValidationLeavesAllAuthoredFilesUnchanged()
    {
        WithDiagram((d, shared) =>
        {
            var trio = File.ReadAllText(d.Trio);
            File.WriteAllText(d.Edited, Svg);
            Assert.Throws<InvalidOperationException>(() => d.SaveToWorkspace(new("type New", "New A", "invalid", "seed", Svg),
                (_, _) => throw new InvalidOperationException("Compiler failed")));
            Assert.Equal(trio, File.ReadAllText(d.Trio));
            Assert.Equal(Svg, File.ReadAllText(d.Edited));
            Assert.Equal("type Original", File.ReadAllText(shared));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(d.Trio)!, ".history")));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(d.Trio)!, "diagram.saved.domain")));
        });
    }

    private static void WithDiagram(Action<PenroseDiagram, string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "ANK save " + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "figures", "penrose", "схема");
        Directory.CreateDirectory(folder);
        var shared = Path.Combine(folder, "..", "shared.domain");
        File.WriteAllText(shared, "type Original");
        File.WriteAllText(Path.Combine(folder, "original.substance"), "Original A");
        File.WriteAllText(Path.Combine(folder, "original.style"), "canvas {}");
        var trio = Path.Combine(folder, "diagram.trio.json");
        File.WriteAllText(trio, """{"domain":"../shared.domain","substance":"original.substance","style":["original.style"]}""");
        try { test(Assert.Single(PenroseDiagram.Find(root)), shared); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
