using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicDocx.Core;

namespace AcademicDocx;

// Hosts the official editor and Roger, not an ANK diagram editor. Both processes
// belong to this menu session and are stopped together when it closes.
internal sealed class PenroseEditor : IDisposable
{
    internal const string Url = "http://127.0.0.1:3000/try/";
    private readonly List<Process> processes = [];
    private readonly object gate = new();
    private readonly object logGate = new();
    private readonly string logPath;
    private readonly EditorProcessJob job = new();
    private EditorSaveApi? saveApi;
    internal bool IsRunning => processes.Count == 2 && processes.All(p => !p.HasExited);

    private PenroseEditor(string project)
    {
        logPath = Path.Combine(project, ".quarto", "penrose-editor", "session.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, "");
        AppDomain.CurrentDomain.ProcessExit += OnExit;
        Console.CancelKeyPress += OnCancel;
    }

    private void OnExit(object? sender, EventArgs args) => Dispose();
    private void OnCancel(object? sender, ConsoleCancelEventArgs args) => Dispose();

    internal static void Install(string root)
    {
        var tools = new LocalToolchain(root);
        if (tools.Node is null || !File.Exists(tools.Node)) throw new FileNotFoundException("Сначала выполните ANK-DOCX.cmd setup.");
        var source = Path.Combine(root, "tools", "PenroseEditor");
        var target = Path.Combine(tools.Tools, "penrose-editor");
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "source.json")));
        var settings = config.RootElement;
        var commit = settings.GetProperty("commit").GetString()!;
        var files = new[] { "package.json", "package-lock.json", "vite.local.mjs", "examples.local.mjs", "tsconfig.json", "SaveToWork.tsx" };
        var fingerprint = commit + string.Join(":", files.Select(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, f))))));
        var marker = Path.Combine(target, ".installed-lock");
        if (File.Exists(marker) && File.ReadAllText(marker) == fingerprint && File.Exists(Path.Combine(target, "node_modules", "vite", "bin", "vite.js"))) return;
        var archive = Path.Combine(tools.Tools, "downloads", "penrose-editor-3.3.1.zip");
        EnvironmentSetup.Download(settings.GetProperty("url").GetString()!, archive);
        EnvironmentSetup.RequireHash(archive, settings.GetProperty("sha256").GetString()!);
        Directory.CreateDirectory(target);
        using (var zip = ZipFile.OpenRead(archive))
        {
            var prefix = "penrose-" + commit + "/packages/editor/";
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0))
            {
                var relative = entry.FullName[prefix.Length..];
                if (relative is "package.json" or "vite.config.ts") continue;
                var destination = Path.GetFullPath(Path.Combine(target, relative));
                if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected editor archive path");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
            zip.GetEntry("penrose-" + commit + "/LICENSE")!.ExtractToFile(Path.Combine(target, "LICENSE"), overwrite: true);
        }
        foreach (var file in files) File.Copy(Path.Combine(source, file), Path.Combine(target, file), overwrite: true);
        var importExport = Path.Combine(target, "src", "components", "ImportExport.tsx");
        var importExportText = File.ReadAllText(importExport);
        if (!importExportText.Contains("<SettingHeader>Export</SettingHeader>", StringComparison.Ordinal))
            throw new InvalidDataException("Проверьте совместимость панели экспорта Penrose.");
        File.Copy(Path.Combine(source, "SaveToWork.tsx"), Path.Combine(target, "src", "components", "SaveToWork.tsx"), overwrite: true);
        File.WriteAllText(importExport, "import SaveToWork from './SaveToWork.js';\n" +
            importExportText.Replace("<SettingHeader>Export</SettingHeader>", "<SaveToWork /><SettingHeader>Export</SettingHeader>", StringComparison.Ordinal));
        // Upstream's stale individual-file listener also receives aggregate
        // trio_files responses. Its AND guard then passes undefined filenames
        // to onSelection. The App already handles the aggregate response.
        var panel = Path.Combine(target, "src", "components", "RogerPanel.tsx");
        var panelText = File.ReadAllText(panel);
        const string guard = "parsed.kind !== \"trio_file\" && parsed.token !== token";
        if (!panelText.Contains(guard, StringComparison.Ordinal)) throw new InvalidDataException("Проверьте совместимость RogerPanel для этой версии редактора.");
        File.WriteAllText(panel, panelText.Replace(guard, "parsed.kind !== \"trio_file\" || parsed.token !== token", StringComparison.Ordinal));
        var npm = Path.Combine(Path.GetDirectoryName(tools.Node)!, "node_modules", "npm", "bin", "npm-cli.js");
        Console.WriteLine("Установка локального редактора Penrose (только при первом запуске / обновлении)...");
        EnvironmentSetup.Execute(tools.Node, [npm, "ci", "--prefix", target, "--legacy-peer-deps", "--no-audit", "--no-fund"], root, root);
        File.WriteAllText(marker, fingerprint);
    }

    internal static PenroseEditor Start(string root, string project, bool openBrowser = true)
    {
        Install(root);
        RequireFree(3000); RequireFree(9160);
        var session = new PenroseEditor(project);
        try
        {
            var tools = new LocalToolchain(root);
            // Upstream watch has a port option but no host option. Keep its logic
            // unchanged except the listener configuration: loopback and the
            // local editor's Origin, rather than arbitrary web pages.
            var original = Path.Combine(Path.GetDirectoryName(tools.PenroseCli)!, "..", "dist", "watch.js");
            var text = File.ReadAllText(original);
            const string binding = "new WebSocketServer({ port })";
            if (!text.Contains(binding, StringComparison.Ordinal)) throw new InvalidDataException("Версия Roger изменилась: нужно проверить локальную привязку сервера.");
            var localWatch = Path.Combine(Path.GetDirectoryName(original)!, "watch-local.js");
            File.WriteAllText(localWatch, text.Replace(binding,
                "new WebSocketServer({ port, host: '127.0.0.1', verifyClient: ({ origin }) => origin === 'http://127.0.0.1:3000' || origin === 'http://localhost:3000' })", StringComparison.Ordinal));
            session.Launch(root, Path.Combine(project, "figures", "penrose"), tools.Node!,
                ["--input-type=module", "-e", "import watch from '" + new Uri(Path.GetFullPath(localWatch)).AbsoluteUri + "'; await watch(9160);"]);
            var editor = Path.Combine(tools.Tools, "penrose-editor");
            session.saveApi = new EditorSaveApi(root, project);
            session.Launch(root, editor, tools.Node!, [Path.Combine(editor, "node_modules", "vite", "bin", "vite.js"), "--config", Path.Combine(editor, "vite.local.mjs")], session.saveApi);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var ready = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (session.processes.Any(p => p.HasExited)) throw new InvalidOperationException("Локальный редактор не запустился. Журнал: " + session.logPath);
                try { if (http.GetAsync(Url + "src/App.tsx").GetAwaiter().GetResult().IsSuccessStatusCode) { ready = true; break; } }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
                Thread.Sleep(250);
            }
            if (!ready) throw new TimeoutException("Редактор Penrose не ответил на localhost. Журнал: " + session.logPath);
            Console.WriteLine("Локальный редактор: " + Url + "\nВо вкладке roger выберите файл trio нужного рисунка.\nПосле правок: Import / Export → Сохранить SVG и Trio в работу. Затем соберите DOCX.\nСессия работает, пока открыто меню рисунков.");
            if (openBrowser) Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true })?.Dispose();
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private void Launch(string root, string directory, string executable, string[] args, EditorSaveApi? api = null)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        new LocalToolchain(root).Configure(start);
        if (api is not null)
        {
            start.Environment["ANK_SAVE_URL"] = api.Address;
            start.Environment["ANK_SAVE_TOKEN"] = api.Token;
        }
        var process = new Process { StartInfo = start };
        void Log(string? text)
        {
            if (text is null) return;
            lock (logGate) File.AppendAllText(logPath, text + Environment.NewLine);
        }
        process.OutputDataReceived += (_, e) => Log(e.Data);
        process.ErrorDataReceived += (_, e) => Log(e.Data);
        process.Start(); processes.Add(process); job.Add(process); process.StandardInput.Close(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
    }

    private static void RequireFree(int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, port);
        try { probe.Start(); }
        catch (SocketException) { throw new IOException($"Порт {port} занят. Закройте другую сессию редактора или приложение на этом порту."); }
        finally { probe.Stop(); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnExit;
            Console.CancelKeyPress -= OnCancel;
            foreach (var process in processes.AsEnumerable().Reverse())
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } }
                catch (InvalidOperationException) { } // Process may exit between HasExited and Kill.
                finally { process.Dispose(); }
            }
            processes.Clear();
            saveApi?.Dispose(); saveApi = null;
            job.Dispose();
        }
    }
}
