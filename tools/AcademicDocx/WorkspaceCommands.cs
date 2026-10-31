using System.Diagnostics;
using System.Text;
using AcademicDocx.Core;

namespace AcademicDocx;

// Convenience commands only: Quarto and renv still own project creation/build.
internal static class WorkspaceCommands
{
    public static void Run(string root, string[] args)
    {
        root = Path.GetFullPath(root);
        if (args.Length == 0)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
            WorkspaceMenu.Run(root);
        }
        else if (args is ["help"] or ["--help"] or ["-h"])
            Console.WriteLine("ANK-DOCX.cmd — интерактивное меню\nANK-DOCX.cmd setup | new NAME [--profile PROFILE] | build NAME | list | profiles | penrose [ROGER_ARGUMENTS]");
        else if (args is ["list"]) List(root);
        else if (args is ["profiles"])
        {
            foreach (var (id, profile) in Profiles(root).Profiles)
                Console.WriteLine(id + " — " + profile.Label + "\n  " + profile.Description);
        }
        else if (args is ["new", var name]) Create(root, name);
        else if (args is ["new", var profiledName, "--profile", var profile]) Create(root, profiledName, profile);
        else if (args is ["build", var buildName]) Build(root, buildName);
        else if (args is ["penrose", .. var penroseArgs])
        {
            var environment = new LocalToolchain(root);
            if (environment.Node is null || !File.Exists(environment.Node) || !File.Exists(environment.PenroseCli))
                throw new FileNotFoundException("Penrose is missing. Run ANK-DOCX.cmd setup first.");
            Execute(root, Environment.CurrentDirectory, environment.Node, [environment.PenroseCli, .. penroseArgs]);
        }
        else throw new ArgumentException("Usage: ANK-DOCX.cmd new NAME [--profile PROFILE] | build NAME | list | profiles | penrose [ROGER_ARGUMENTS]");
    }

    internal static string Project(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Length > 120)
            throw new ArgumentException("Invalid workspace name: use one folder name, not a path");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]))
            throw new ArgumentException("Invalid workspace name: reserved Windows name");
        var workspaces = Path.GetFullPath(Path.Combine(root, "Workspaces"));
        var project = Path.GetFullPath(Path.Combine(workspaces, name));
        if (!project.StartsWith(workspaces + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Workspace must stay inside Workspaces");
        return project;
    }

    internal static AcademicProfiles Profiles(string root) =>
        AcademicProfiles.Read(Path.Combine(root, "quarto", "_extensions", "academic", "profiles.json"));

    internal static void Create(string root, string name, string? profile = null)
    {
        var project = Project(root, name);
        if (Directory.Exists(project) || File.Exists(project)) throw new IOException("Workspace already exists; nothing was changed: " + project);
        var catalog = Profiles(root);
        var selected = catalog.Resolve(profile);
        var quarto = Quarto(root);
        var r = Tool(root, "R", "bin", "Rscript.exe");
        Directory.CreateDirectory(project);
        Execute(root, project, quarto, ["use", "template", Path.Combine(root, "quarto"), "--no-prompt"]);
        // Only the known, freshly generated Starter metadata is changed here.
        var metadata = Path.Combine(project, "metadata.yml");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("profile: " + catalog.Default, "profile: " + selected, StringComparison.Ordinal));
        Execute(root, project, r, ["--vanilla", "tools/install.R"]);
        Console.WriteLine("Создано: " + project);
        Console.WriteLine("Заполните metadata.yml и chapters/main.qmd, затем выберите «Собрать DOCX» в меню ANK-DOCX.cmd.");
    }

    internal static void Build(string root, string name)
    {
        var project = Project(root, name);
        if (!File.Exists(Path.Combine(project, "_quarto.yml")) || !File.Exists(Path.Combine(project, "chapters", "main.qmd")))
            throw new DirectoryNotFoundException("Workspace not found: " + project);
        Execute(root, project, Quarto(root), ["render", "--to", "academic-docx"]);
        Console.WriteLine("Результат: " + Path.Combine(project, "_output", "chapters", "main.docx"));
    }

    private static void List(string root)
    {
        var projects = Projects(root);
        if (projects.Length == 0) Console.WriteLine("Работ пока нет. Выберите «Создать работу» в меню ANK-DOCX.cmd.");
        foreach (var project in projects) Console.WriteLine(project);
    }

    internal static string[] Projects(string root)
    {
        var folder = Path.Combine(root, "Workspaces");
        return Directory.Exists(folder) ? Directory.GetDirectories(folder)
            .Where(p => File.Exists(Path.Combine(p, "_quarto.yml")) && File.Exists(Path.Combine(p, "chapters", "main.qmd")))
            .Select(p => Path.GetFileName(p)).Order(StringComparer.CurrentCultureIgnoreCase).ToArray() : [];
    }

    private static string Tool(string root, params string[] path)
    {
        var tool = Path.Combine(new[] { root, ".tools" }.Concat(path).ToArray());
        if (!File.Exists(tool)) throw new FileNotFoundException("Run ANK-DOCX.cmd setup first. Missing tool: " + tool);
        return tool;
    }

    private static string Quarto(string root)
    {
        return QuartoLauncher.Executable(root);
    }

    private static void Execute(string root, string project, string executable, string[] args)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        new LocalToolchain(root).Configure(start);
        start.Environment["PATH"] = Path.GetDirectoryName(executable) + Path.PathSeparator + start.Environment["PATH"];
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, item) => { if (item.Data is not null) Console.WriteLine(item.Data); };
        process.ErrorDataReceived += (_, item) => { if (item.Data is not null) Console.Error.WriteLine(item.Data); };
        if (!process.Start()) throw new InvalidOperationException("Cannot start " + executable);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} failed (exit {process.ExitCode}). Project files kept at {project}");
    }
}
