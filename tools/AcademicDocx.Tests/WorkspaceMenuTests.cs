using System.Diagnostics;
using System.Text;

namespace AcademicDocx.Tests;

public sealed class WorkspaceMenuTests
{
    [WordTheory]
    [InlineData("1\n0\n0\n")]
    [InlineData("1\n2\n\n0\n")]
    [InlineData("1\n2\n")]
    public void CancellingCreationOrClosingInputDoesNotCreateAWorkspace(string input)
    {
        WithRoot(root =>
        {
            var result = Run(root, input);
            Assert.Equal(0, result.Code);
            Assert.Contains("Техническая курсовая с рамками", result.Output);
            Assert.False(Directory.Exists(Path.Combine(root, "Workspaces")));
        });
    }

    [WordFact]
    public void ExistingNameIsRetriedAndAuthoredFilesAreKept()
    {
        WithRoot(root =>
        {
            var project = Path.Combine(root, "Workspaces", "Курсовая [тест]");
            Directory.CreateDirectory(project);
            var authored = Path.Combine(project, "metadata.yml");
            File.WriteAllText(authored, "author: Не изменять");
            var result = Run(root, "1\n2\nКурсовая [тест]\n\n0\n");
            Assert.Equal(0, result.Code);
            Assert.Contains("Такая папка уже существует", result.Output);
            Assert.Equal("author: Не изменять", File.ReadAllText(authored));
            Assert.Single(Directory.GetFiles(project));
        });
    }

    [WordFact]
    public void BuildSelectsExistingWorkByNumberAndReturnsToMenuOnFailure()
    {
        WithRoot(root =>
        {
            var project = Path.Combine(root, "Workspaces", "Курсовая [тест]");
            Directory.CreateDirectory(Path.Combine(project, "chapters"));
            File.WriteAllText(Path.Combine(project, "_quarto.yml"), "project: default");
            File.WriteAllText(Path.Combine(project, "chapters", "main.qmd"), "Текст");
            var result = Run(root, "2\n1\n0\n");
            Assert.Equal(0, result.Code);
            Assert.Contains("1. Курсовая [тест]", result.Output);
            Assert.Contains("Не удалось выполнить действие", result.Output);
            Assert.Equal("Текст", File.ReadAllText(Path.Combine(project, "chapters", "main.qmd")));
            Assert.Equal(2, result.Output.Split("Что сделать?", StringSplitOptions.None).Length - 1);
        });
    }

    [WordFact]
    public void InvalidMenuInputIsRetriedAndMissingDocumentDoesNotLaunchWord()
    {
        WithRoot(root =>
        {
            var project = Path.Combine(root, "Workspaces", "Реферат");
            Directory.CreateDirectory(Path.Combine(project, "chapters"));
            File.WriteAllText(Path.Combine(project, "_quarto.yml"), "project: default");
            File.WriteAllText(Path.Combine(project, "chapters", "main.qmd"), "Текст");
            var result = Run(root, "99\n4\n1\n0\n");
            Assert.Equal(0, result.Code);
            Assert.Contains("Выберите номер из списка", result.Output);
            Assert.Contains("Готового DOCX ещё нет", result.Output);
        });
    }

    private static void WithRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "ANK меню " + Guid.NewGuid().ToString("N"));
        var extension = Path.Combine(root, "quarto", "_extensions", "academic");
        Directory.CreateDirectory(extension);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "profiles.json"), Path.Combine(extension, "profiles.json"));
        try { test(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static (int Code, string Output) Run(string root, string input)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE")!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "workspace", root }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        if (!process.WaitForExit(20_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Workspace menu timed out"); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
