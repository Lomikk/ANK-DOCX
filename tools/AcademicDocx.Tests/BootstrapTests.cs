using System.Diagnostics;

namespace AcademicDocx.Tests;

public sealed class BootstrapTests
{
    [WordFact]
    public void QuartoAliasPreservesLiteralUnicodeAndShellCharactersInTheTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "ANK & %TEMP% ! Кириллица " + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, ".tools", "quarto-1.10.19");
        Directory.CreateDirectory(Path.Combine(target, "bin"));
        string? alias = null;
        try
        {
            File.WriteAllText(Path.Combine(target, "bin", "quarto.exe"), "fixture; not executed");
            File.WriteAllText(Path.Combine(root, "dependencies.lock.json"), """
                {"quarto":{"version":"1.10.19"},"svg_converter":{"distribution_release":"v1.0.0"}}
                """);
            string Resolve()
            {
                var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE")!)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                foreach (var arg in new[] { "quarto-path", root }) info.ArgumentList.Add(arg);
                using var process = Process.Start(info)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                Assert.True(process.WaitForExit(20_000), "Alias command timed out");
                Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
                return output.GetAwaiter().GetResult().Trim();
            }
            var executable = Resolve();
            alias = Path.GetDirectoryName(Path.GetDirectoryName(executable)!)!;
            Assert.StartsWith(Path.Combine(Path.GetTempPath(), "ank-quarto-"), alias);
            Assert.Equal(target, new DirectoryInfo(alias).ResolveLinkTarget(false)!.FullName);
            Assert.Equal(executable, Resolve());
        }
        finally
        {
            if (alias is not null) Directory.Delete(alias);
            Directory.Delete(root, recursive: true);
        }
    }

    [WordTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BootstrapRequiresThePinnedArchiveAndNoSystemDotnet(bool corrupt)
    {
        var source = Environment.GetEnvironmentVariable("ACADEMIC_TEMPLATE_SOURCE") ?? throw new InvalidOperationException("Template source is required");
        var archive = Environment.GetEnvironmentVariable("ACADEMIC_SERVICE_ARCHIVE") ?? throw new InvalidOperationException("Release archive is required");
        var repository = Path.GetFullPath(Path.Combine(source, ".."));
        var root = Path.Combine(Path.GetTempPath(), "ANK установка " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        try
        {
            File.Copy(Path.Combine(repository, "setup-academic.cmd"), Path.Combine(root, "setup-academic.cmd"));
            File.Copy(Path.Combine(repository, "tools", "service-release.cmd"), Path.Combine(root, "tools", "service-release.cmd"));
            if (corrupt)
            {
                archive = Path.Combine(root, "invalid.zip");
                File.WriteAllText(archive, "This is not the published release");
            }
            var first = Install(root, archive);
            var service = Path.Combine(root, ".tools", "academic-docx", "academic-docx.exe");
            if (corrupt)
            {
                Assert.NotEqual(0, first.Code);
                Assert.Contains("checksum mismatch", first.Output);
                Assert.False(File.Exists(service));
                return;
            }
            Assert.True(first.Code == 0, first.Output);
            Assert.True(File.Exists(service));
            Assert.True(File.Exists(Path.Combine(root, ".tools", "academic-docx", "coreclr.dll")));
            var before = File.GetLastWriteTimeUtc(service);
            var second = Install(root, archive);
            Assert.True(second.Code == 0, second.Output);
            Assert.Equal(before, File.GetLastWriteTimeUtc(service));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static (int Code, string Output) Install(string root, string archive)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var info = new ProcessStartInfo(Path.Combine(system, "cmd.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "/d", "/c", "setup-academic.cmd", "--service-only" }) info.ArgumentList.Add(argument);
        info.Environment["PATH"] = system;
        info.Environment["DOTNET_ROOT"] = Path.Combine(root, "missing-dotnet");
        info.Environment["ANK_DOCX_SERVICE_ARCHIVE"] = archive;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(); throw new TimeoutException("Bootstrap timed out"); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
