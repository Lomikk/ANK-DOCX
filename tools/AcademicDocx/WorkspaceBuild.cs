using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcademicDocx.Core;

namespace AcademicDocx;

internal static class WorkspaceBuild
{
    public static int Run(string root, string project, bool verbose)
    {
        var folder = Path.Combine(project, ".quarto");
        Directory.CreateDirectory(folder);
        // Open both journals with overwrite semantics before any build work.
        using var summary = new StreamWriter(Path.Combine(folder, "build-summary.log"), append: false, new UTF8Encoding(false)) { AutoFlush = true };
        using var full = new StreamWriter(Path.Combine(folder, "build.log"), append: false, new UTF8Encoding(false)) { AutoFlush = true };
        var reportPath = Path.Combine(folder, "main.docx.finalize.json");
        var diagnostics = new List<string>();
        var exitCode = 1;
        try
        {
            // A failed pre-render must not be explained using an earlier Word report.
            File.Delete(reportPath);
            if (!File.Exists(Path.Combine(project, "_quarto.yml")) || !File.Exists(Path.Combine(project, "chapters", "main.qmd")))
                throw new FileNotFoundException("Workspace needs _quarto.yml and chapters/main.qmd: " + project);
            var start = new ProcessStartInfo(QuartoLauncher.Executable(root))
            {
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "render", "--to", "academic-docx" })
                start.ArgumentList.Add(arg);
            new LocalToolchain(root).Configure(start);
            var gate = new object();
            void Output(string? line, bool error)
            {
                if (line is null) return;
                lock (gate)
                {
                    full.WriteLine(line);
                    if (verbose) (error ? Console.Error : Console.Out).WriteLine(line);
                }
            }
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, item) => Output(item.Data, false);
            process.ErrorDataReceived += (_, item) => Output(item.Data, true);
            if (!process.Start()) throw new InvalidOperationException("Cannot start Quarto");
            process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
            exitCode = process.ExitCode;
            if (exitCode != 0 && File.Exists(reportPath))
            {
                using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
                if (report.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                    diagnostics.Add(BuildDiagnostics.Compact(error.GetString()!));
            }
            diagnostics.AddRange(BuildDiagnostics.Read(Path.Combine(folder, "build.log")));
        }
        catch (Exception error)
        {
            if (exitCode == 0) exitCode = 1;
            full.WriteLine(error);
            diagnostics.Add(BuildDiagnostics.Compact(error.Message));
        }

        void Write(string line) { Console.WriteLine(line); summary.WriteLine(line); }
        if (exitCode != 0) Write("Ошибка сборки (код " + exitCode + ").");
        var lines = diagnostics.Where(message => message.Length != 0).Distinct(StringComparer.Ordinal)
            .SelectMany(message => message.Split(Environment.NewLine)).ToArray();
        foreach (var line in lines.Take(30)) Write(line);
        if (lines.Length > 30) Write("Остальные сообщения сохранены в полном журнале.");
        if (exitCode == 0 && lines.Length == 0) Write("Ошибок и предупреждений сборки не обнаружено.");
        if (exitCode != 0 || lines.Length > 30) Write("Полный журнал: " + Path.Combine(folder, "build.log"));
        return exitCode;
    }
}
