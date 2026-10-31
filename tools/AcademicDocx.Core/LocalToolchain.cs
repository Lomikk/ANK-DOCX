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

    public LocalToolchain(string root)
    {
        Root = Path.GetFullPath(root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "dependencies.lock.json")));
        var data = manifest.RootElement;
        Quarto = Path.Combine(Tools, "quarto-" + data.GetProperty("quarto").GetProperty("version").GetString(), "bin", "quarto.exe");
        Svg = Path.Combine(Tools, "rsvg-convert-static-" + data.GetProperty("svg_converter").GetProperty("distribution_release").GetString()!.TrimStart('v'), "rsvg-convert.exe");
    }

    public void Configure(ProcessStartInfo process)
    {
        process.Environment["PATH"] = string.Join(Path.PathSeparator,
            new[] { Service, Quarto, R, Svg }.Select(p => Path.GetDirectoryName(p)!)) + Path.PathSeparator + process.Environment["PATH"];
        process.Environment["QUARTO_R"] = R;
        process.Environment["ACADEMIC_DOCX_SERVICE"] = Service;
        process.Environment["RENV_PATHS_ROOT"] = Renv;
        foreach (var name in new[] { "CACHE", "SOURCE", "BINARY", "SANDBOX" })
            process.Environment["RENV_PATHS_" + name] = Path.Combine(Renv, name.ToLowerInvariant());
        foreach (var name in new[] { "RENV_PATHS_LIBRARY", "RENV_PATHS_LIBRARY_ROOT", "LC_ALL", "LC_CTYPE", "LC_COLLATE", "LC_MONETARY", "LC_TIME" })
            process.Environment.Remove(name);
    }
}
