using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AcademicDocx.Core;

// Filenames are the state: no second catalog or manual/automatic registry.
public sealed record PenroseDiagram(string Project, string Id, string Trio)
{
    public string Edited => Trio[..^10] + ".edited.svg"; // remove .trio.json
    public string Output => Path.Combine(Project, ".quarto", "penrose", Id.Replace('/', Path.DirectorySeparatorChar) + ".svg");
    public bool HasEdits => File.Exists(Edited);

    public static PenroseDiagram[] Find(string project)
    {
        project = Path.GetFullPath(project);
        var folder = Path.Combine(project, "figures", "penrose");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.trio.json", new EnumerationOptions
            { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(path => new PenroseDiagram(project, Path.GetRelativePath(folder, path)[..^10].Replace('\\', '/'), path))
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static XDocument ReadSvg(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 50_000_000 });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != XName.Get("svg", "http://www.w3.org/2000/svg"))
            throw new InvalidDataException("Выберите SVG-файл, экспортированный из редактора.");
        return document;
    }

    // Official editor's SVG upload reads these metadata fields. The renderer still
    // owns every visual element; we only attach its source programs for re-opening.
    public void AttachEditorSources(string svg)
    {
        var document = ReadSvg(svg);
        using var trio = JsonDocument.Parse(File.ReadAllText(Trio));
        var data = trio.RootElement;
        var folder = Path.GetDirectoryName(Trio)!;
        string Read(string path) => File.ReadAllText(Path.GetFullPath(Path.Combine(folder, path)));
        var metadata = document.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "penrose");
        if (metadata is null) { metadata = new XElement("penrose"); document.Root.AddFirst(metadata); }
        // Keep metadata of an already exported edit intact, including any edited source.
        void Add(string name, string value)
        {
            if (!metadata.Elements().Any(e => e.Name.LocalName == name)) metadata.Add(new XElement(name, value));
        }
        Add("dsl", Read(data.GetProperty("domain").GetString()!));
        Add("sub", Read(data.GetProperty("substance").GetString()!));
        Add("sty", string.Join("\n", data.GetProperty("style").EnumerateArray().Select(e => Read(e.GetString()!))));
        Add("variation", data.TryGetProperty("variation", out var variation) ? variation.GetString() ?? "" : "");
        Add("version", "3.3.1");
        document.Save(svg, SaveOptions.DisableFormatting);
    }

    public void Apply(string source)
    {
        _ = ReadSvg(source);
        var bytes = File.ReadAllBytes(source);
        var temporary = Edited + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            Backup();
            File.Move(temporary, Edited, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    // Each diagram gets a private source snapshot. In particular, saving a
    // combined Style from the editor must not overwrite a shared library.
    public void SaveToWorkspace(PenroseSource source, Action<string, string> validate)
    {
        var folder = Path.GetDirectoryName(Trio)!;
        var stem = Path.GetFileName(Trio)[..^10];
        var domain = stem + ".saved.domain";
        var substance = stem + ".saved.substance";
        var style = stem + ".saved.style";
        var descriptor = JsonNode.Parse(File.ReadAllText(Trio))!.AsObject();
        descriptor["domain"] = domain;
        descriptor["substance"] = substance;
        descriptor["style"] = new JsonArray(JsonValue.Create(style));
        descriptor["variation"] = source.Variation;
        var changes = new Dictionary<string, byte[]>
        {
            [Path.Combine(folder, domain)] = Encoding.UTF8.GetBytes(source.Domain),
            [Path.Combine(folder, substance)] = Encoding.UTF8.GetBytes(source.Substance),
            [Path.Combine(folder, style)] = Encoding.UTF8.GetBytes(source.Style),
            [Trio] = Encoding.UTF8.GetBytes(descriptor.ToJsonString(new JsonSerializerOptions { WriteIndented = true })),
        };
        var staging = Path.Combine(Project, ".quarto", "penrose-save", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (path, bytes) in changes) File.WriteAllBytes(Path.Combine(staging, Path.GetFileName(path)), bytes);
            var svgPath = Path.Combine(staging, "edited.svg");
            File.WriteAllText(svgPath, source.Svg);
            var svg = ReadSvg(svgPath);
            var metadata = svg.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "penrose");
            if (metadata is null) { metadata = new XElement("penrose"); svg.Root.AddFirst(metadata); }
            foreach (var (name, value) in new[] { ("dsl", source.Domain), ("sub", source.Substance), ("sty", source.Style), ("variation", source.Variation) })
            {
                metadata.Elements().Where(e => e.Name.LocalName == name).Remove();
                metadata.Add(new XElement(name, value));
            }
            svg.Save(svgPath);
            validate(Path.Combine(staging, Path.GetFileName(Trio)), svgPath);
            changes[Edited] = File.ReadAllBytes(svgPath);
            var previous = changes.Keys.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            var history = Path.Combine(folder, ".history");
            Directory.CreateDirectory(history);
            using (var zip = ZipFile.Open(Path.Combine(history, stem + "-save-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip"), ZipArchiveMode.Create))
            {
                foreach (var (path, bytes) in previous.Where(item => item.Value is not null))
                {
                    using var entry = zip.CreateEntry(Path.GetFileName(path)).Open();
                    entry.Write(bytes!);
                }
                // Include referenced originals too, even when they are shared
                // outside this diagram's directory. History remains self-contained.
                using var old = JsonDocument.Parse(File.ReadAllBytes(Trio));
                var references = new[] { old.RootElement.GetProperty("domain").GetString()!, old.RootElement.GetProperty("substance").GetString()! }
                    .Concat(old.RootElement.GetProperty("style").EnumerateArray().Select(e => e.GetString()!)).ToArray();
                for (var i = 0; i < references.Length; i++)
                {
                    var path = Path.GetFullPath(Path.Combine(folder, references[i]));
                    if (!File.Exists(path)) continue;
                    using var entry = zip.CreateEntry("referenced/" + i + "/" + Path.GetFileName(path)).Open();
                    using var original = File.OpenRead(path); original.CopyTo(entry);
                }
            }
            try { foreach (var (path, bytes) in changes) WriteAtomic(path, bytes); }
            catch
            {
                foreach (var (path, bytes) in previous)
                    if (bytes is null) File.Delete(path); else WriteAtomic(path, bytes);
                throw;
            }
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, overwrite: true); }
        finally { File.Delete(temporary); }
    }

    public void UseAutomatic(Action generate)
    {
        generate();
        Backup();
        File.Delete(Edited);
    }

    private void Backup()
    {
        if (!HasEdits) return;
        var history = Path.Combine(Path.GetDirectoryName(Edited)!, ".history");
        Directory.CreateDirectory(history);
        File.Copy(Edited, Path.Combine(history, Path.GetFileNameWithoutExtension(Edited) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".svg"));
    }
}
