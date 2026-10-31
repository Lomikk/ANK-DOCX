using System.Diagnostics;
using System.Text.Json;

namespace AcademicDocx.Core;

// One process-local toolchain for setup, new and build. Project libraries and
// lockfiles still belong to renv; no package manager is implemented here.
public sealed class LocalToolchain
{
    public string Root { get; }
    public string Tools => Path.Combine(Root, ".tools");
    public string R => Path.Combine(Tools, "R", "bin", "Rscript.exe");
    public string Quarto { get; }
    public string Svg { get; }
    public string Service => Path.Combine(Tools, "academic-docx", "academic-docx.exe");
    public string Renv => Path.Combine(Tools, "renv");
    public string? Node { get; }
    public string PenroseCli => Path.Combine(Tools, "penrose", "node_modules", "@penrose", "roger", "bin", "run.js");

    public LocalToolchain(string root)
    {
        Root = Path.GetFullPath(root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "dependencies.lock.json")));
        var data = manifest.RootElement;
        Quarto = Path.Combine(Tools, "quarto-" + data.GetProperty("quarto").GetProperty("version").GetString(), "bin", "quarto.exe");
        Svg = Path.Combine(Tools, "rsvg-convert-static-" + data.GetProperty("svg_converter").GetProperty("distribution_release").GetString()!.TrimStart('v'), "rsvg-convert.exe");
        if (data.TryGetProperty("penrose", out var penrose))
            Node = Path.Combine(Tools, "node-v" + penrose.GetProperty("node_version").GetString() + "-win-x64", "node.exe");
    }

    public void Configure(ProcessStartInfo process)
    {
        process.Environment["PATH"] = string.Join(Path.PathSeparator,
            new[] { Service, Quarto, R, Svg }.Select(p => Path.GetDirectoryName(p)!)) + Path.PathSeparator + process.Environment["PATH"];
        process.Environment["QUARTO_R"] = R;
        process.Environment["ACADEMIC_DOCX_SERVICE"] = Service;
        process.Environment["ANK_SVG_CONVERTER"] = Svg;
        process.Environment["RENV_PATHS_ROOT"] = Renv;
        if (Node is not null)
        {
            process.Environment["PATH"] = Path.GetDirectoryName(Node) + Path.PathSeparator + process.Environment["PATH"];
            process.Environment["ANK_PENROSE_NODE"] = Node;
            process.Environment["ANK_PENROSE_CLI"] = PenroseCli;
            process.Environment["npm_config_cache"] = Path.Combine(Tools, "npm-cache");
        }
        foreach (var name in new[] { "CACHE", "SOURCE", "BINARY", "SANDBOX" })
            process.Environment["RENV_PATHS_" + name] = Path.Combine(Renv, name.ToLowerInvariant());
        foreach (var name in new[] { "INIT_CWD", "RENV_PATHS_LIBRARY", "RENV_PATHS_LIBRARY_ROOT", "LC_ALL", "LC_CTYPE", "LC_COLLATE", "LC_MONETARY", "LC_TIME" })
            process.Environment.Remove(name);
    }
}
