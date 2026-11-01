using System.Diagnostics;
using System.Text;

namespace AcademicDocx.Tests;

public sealed class PenroseBuildTests
{
    [Fact]
    public async Task SavedEditBuildsWithoutPenroseEvenWhenSourceIsBrokenAndIsNeverRewritten()
    {
        var root = FindRoot();
        var r = Path.Combine(root, ".tools", "R", "bin", "Rscript.exe");
        var converter = new AcademicDocx.Core.LocalToolchain(root).Svg;
        // Integration check requires the installed local toolchain, not Word.
        if (!File.Exists(r) || !File.Exists(converter)) return;
        var project = Path.Combine(Path.GetTempPath(), "ANK edited SVG " + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(project, "figures", "penrose", "тест");
        Directory.CreateDirectory(folder);
        var edited = Path.Combine(folder, "diagram.edited.svg");
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"><text x=\"10\" y=\"40\">ПРАВКА</text></svg>";
        File.WriteAllText(edited, svg);
        File.WriteAllText(Path.Combine(folder, "diagram.trio.json"), "broken source deliberately ignored");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var info = new ProcessStartInfo(r) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                info.ArgumentList.Add("--vanilla"); info.ArgumentList.Add(Path.Combine(root, "quarto", "_extensions", "academic", "penrose.R"));
                new AcademicDocx.Core.LocalToolchain(root).Configure(info);
                info.Environment["ANK_PENROSE_NODE"] = "deliberately-missing-node";
                info.Environment["ANK_PENROSE_CLI"] = "deliberately-missing-roger";
                info.Environment["ANK_SVG_CONVERTER"] = converter;
                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20_000)) { process.Kill(true); throw new TimeoutException(); }
                Assert.True(process.ExitCode == 0, await stdout + await stderr);
                Assert.Equal(svg, File.ReadAllText(edited));
                Assert.True(new FileInfo(Path.Combine(project, ".quarto", "penrose", "тест", "diagram.svg")).Length > 0);
            }
        }
        finally { Directory.Delete(project, recursive: true); }
    }

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "dependencies.lock.json"))) return folder.FullName;
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
