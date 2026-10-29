using System.Diagnostics;
using System.Security.Cryptography;

namespace AcademicDocx.Tests;

public sealed class CliTests
{
    [WordTheory]
    [InlineData("finalize-word")]
    [InlineData("check-word")]
    [InlineData("inspect-word")]
    [InlineData("export-pdf")]
    public void ReportCannotOverwriteTheInputDocument(string command)
    {
        var folder = Path.Combine(Path.GetTempPath(), "academic-cli-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var input = Path.Combine(folder, "input.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "reference.docx"), input);
        var before = SHA256.HashData(File.ReadAllBytes(input));
        try
        {
            var arguments = command switch
            {
                "finalize-word" => new[] { command, input, "--report", input },
                "check-word" => [command, input, Path.Combine(folder, "copy.docx"), input],
                "export-pdf" => [command, input, Path.Combine(folder, "out.pdf"), input],
                _ => [command, input, input]
            };
            var result = Run(arguments);
            Assert.NotEqual(0, result.Code);
            Assert.Contains("distinct paths", result.Output);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(input)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [WordFact]
    public void PublishedApplicationRunsWithoutDotnetRootOrPath()
    {
        var result = Run(["help"], standalone: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("Academic DOCX service", result.Output);
    }

    [WordTheory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    public void WorkspaceRejectsPathsAndReservedNames(string name)
    {
        var result = Run(["workspace", Path.GetTempPath(), "new", name]);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("Invalid workspace name", result.Output);
    }

    [WordFact]
    public void NewWorkspaceDoesNotOverwriteExistingAuthoredFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "academic-workspace-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Workspaces", "Existing");
        Directory.CreateDirectory(project);
        var authored = Path.Combine(project, "main.qmd");
        File.WriteAllText(authored, "Author text");
        try
        {
            var result = Run(["workspace", root, "new", "Existing"]);
            Assert.NotEqual(0, result.Code);
            Assert.Contains("already exists", result.Output);
            Assert.Equal("Author text", File.ReadAllText(authored));
            Assert.Single(Directory.GetFiles(project));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static (int Code, string Output) Run(string[] args, bool standalone = false)
    {
        var exe = Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE") ?? throw new InvalidOperationException("Service path is required");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (standalone)
        {
            info.Environment["DOTNET_ROOT"] = Path.Combine(Path.GetTempPath(), "missing-dotnet-root");
            info.Environment["PATH"] = "";
        }
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000)) { process.Kill(); throw new TimeoutException("Service CLI timed out"); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
