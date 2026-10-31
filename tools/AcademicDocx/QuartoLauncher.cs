using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AcademicDocx.Core;

namespace AcademicDocx;

internal static class QuartoLauncher
{
    public static string Executable(string root)
    {
        var executable = new LocalToolchain(root).Quarto;
        if (!File.Exists(executable)) throw new FileNotFoundException("Run ANK-DOCX.cmd setup first. Missing tool: " + executable);
        var target = Path.GetDirectoryName(Path.GetDirectoryName(executable)!)!;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target)))[..12];
        var alias = Path.Combine(Path.GetTempPath(), "ank-" + Path.GetFileName(target) + "-" + key);
        if (alias.Any(c => c > 127))
            throw new InvalidOperationException("Quarto currently requires an ASCII TEMP path for its runtime alias. Set TEMP to a writable ASCII path.");
        if (Directory.Exists(alias))
        {
            var linked = new DirectoryInfo(alias).ResolveLinkTarget(returnFinalTarget: false);
            if (linked is null || !Path.GetFullPath(linked.FullName).Equals(target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Quarto alias path is occupied: " + alias);
        }
        else
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = "/d /v:off /c mklink /J \"%ANK_LINK%\" \"%ANK_TARGET%\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            // Fixed command; quoted paths enter through environment expansion,
            // with delayed expansion disabled and no CALL/double expansion.
            start.Environment["ANK_LINK"] = alias;
            start.Environment["ANK_TARGET"] = target;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Cannot create Quarto junction: " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
        return Path.Combine(alias, "bin", "quarto.exe");
    }
}
