using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicDocx.Core;

namespace AcademicDocx;

internal static class EnvironmentSetup
{
    public static void Run(string root, bool development = false)
    {
        root = Path.GetFullPath(root);
        var lockPath = Path.Combine(root, "dependencies.lock.json");
        var template = Path.Combine(root, "quarto");
        var installer = Path.Combine(template, "tools", "install.R");
        if (!File.Exists(lockPath) || !File.Exists(installer)) throw new ArgumentException("Setup root must be an Academic DOCX repository");
        if (Type.GetTypeFromProgID("Word.Application") is null)
            throw new InvalidOperationException("Microsoft Word desktop is required. Install Word, then run ANK-DOCX.cmd setup again.");
        var environment = new LocalToolchain(root);
        using var dependencies = JsonDocument.Parse(File.ReadAllText(lockPath));
        var data = dependencies.RootElement;
        var rVersion = data.GetProperty("r").GetString()!;
        var quarto = data.GetProperty("quarto");
        var quartoVersion = quarto.GetProperty("version").GetString()!;
        var svg = data.GetProperty("svg_converter");
        var nodeVersion = data.GetProperty("penrose").GetProperty("node_version").GetString()!;
        var svgRelease = svg.GetProperty("distribution_release").GetString()!.TrimStart('v');
        var tools = Path.Combine(root, ".tools");
        Directory.CreateDirectory(tools);
        var rRoot = Path.Combine(tools, "R");
        var rScript = Path.Combine(rRoot, "bin", "Rscript.exe");
        if (!File.Exists(rScript))
        {
            var setup = Path.Combine(tools, "downloads", $"R-{rVersion}-win.exe");
            Download($"https://cran.r-project.org/bin/windows/base/old/{rVersion}/R-{rVersion}-win.exe", setup);
            Authenticode.RequireTrusted(setup);
            Execute(setup, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/NOICONS", "/TASKS=", "/DIR=" + rRoot], root, root);
        }
        RequireVersion(rScript, ["--version"], rVersion, exact: false, root);
        // This belongs to our local R installation, never the user's R profile.
        File.WriteAllText(Path.Combine(tools, "R", "etc", "Renviron.site"), $$"""
            # Managed by ANK DOCX setup. Paths remain relative to this R installation.
            RENV_PATHS_ROOT="${RENV_PATHS_ROOT:-${R_HOME}/../renv}"
            RENV_PATHS_CACHE="${RENV_PATHS_CACHE:-${RENV_PATHS_ROOT}/cache}"
            RENV_PATHS_SOURCE="${RENV_PATHS_SOURCE:-${RENV_PATHS_ROOT}/source}"
            RENV_PATHS_BINARY="${RENV_PATHS_BINARY:-${RENV_PATHS_ROOT}/binary}"
            RENV_PATHS_SANDBOX="${RENV_PATHS_SANDBOX:-${RENV_PATHS_ROOT}/sandbox}"
            ANK_PENROSE_NODE="${ANK_PENROSE_NODE:-${R_HOME}/../node-v{{nodeVersion}}-win-x64/node.exe}"
            ANK_PENROSE_CLI="${ANK_PENROSE_CLI:-${R_HOME}/../penrose/node_modules/@penrose/roger/bin/run.js}"
            ANK_SVG_CONVERTER="${ANK_SVG_CONVERTER:-${R_HOME}/../rsvg-convert-static-{{svgRelease}}/rsvg-convert.exe}"
            """ + "\n", new UTF8Encoding(false));
        var quartoRoot = Path.Combine(tools, "quarto-" + quartoVersion);
        var quartoExe = Path.Combine(quartoRoot, "bin", "quarto.exe");
        if (!File.Exists(quartoExe))
        {
            var zip = Path.Combine(tools, "downloads", $"quarto-{quartoVersion}-win.zip");
            Download($"https://github.com/quarto-dev/quarto-cli/releases/download/v{quartoVersion}/quarto-{quartoVersion}-win.zip", zip);
            RequireHash(zip, quarto.GetProperty("windows_zip_sha256").GetString()!);
            ZipFile.ExtractToDirectory(zip, quartoRoot, overwriteFiles: true);
        }
        RequireVersion(quartoExe, ["--version"], quartoVersion, exact: true, root);
        var svgRoot = Path.Combine(tools, "rsvg-convert-static-" + svg.GetProperty("distribution_release").GetString()!.TrimStart('v'));
        var svgExe = Path.Combine(svgRoot, "rsvg-convert.exe");
        if (!File.Exists(svgExe))
        {
            var zip = Path.Combine(tools, "downloads", Path.GetFileName(svgRoot) + ".zip");
            Download(svg.GetProperty("url").GetString()!, zip);
            RequireHash(zip, svg.GetProperty("sha256").GetString()!);
            ZipFile.ExtractToDirectory(zip, svgRoot, overwriteFiles: true);
        }
        RequireVersion(svgExe, ["--version"], svg.GetProperty("version").GetString()!.Replace("librsvg ", "rsvg-convert version ", StringComparison.Ordinal), exact: true, root);
        InstallPenrose(root, data.GetProperty("penrose"), environment);
        // Process-local environment only. Do not change the user's locale/PATH.
        if (development)
        {
            var example = Path.Combine(root, "examples", "book");
            Execute(quartoExe, ["add", template, "--no-prompt"], example, root);
            File.Copy(Path.Combine(template, "tools", "academic.R"), Path.Combine(example, "tools", "academic.R"), overwrite: true);
            Execute(rScript, ["--vanilla", installer], example, root, clearLocale: true);
        }
        var reference = Path.Combine(root, "Workspaces", "Пример");
        // Install the starter's runtime files, not a second maintained implementation.
        foreach (var file in new[] { ".Rprofile", "DESCRIPTION", "renv.lock", "renv/activate.R", "renv/settings.json", "tools/install.R", "tools/academic.R" })
        {
            var destination = Path.Combine(reference, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(template, file), destination, overwrite: true);
        }
        Execute(quartoExe, ["add", template, "--no-prompt"], reference, root);
        Execute(rScript, ["--vanilla", installer], reference, root, clearLocale: true);
        Console.WriteLine("Local dependency cache: " + environment.Renv);
        Console.WriteLine("Academic DOCX environment is ready.");
    }

    private static void InstallPenrose(string root, JsonElement settings, LocalToolchain environment)
    {
        var version = settings.GetProperty("node_version").GetString()!;
        var node = environment.Node!;
        if (!File.Exists(node))
        {
            var archive = Path.Combine(environment.Tools, "downloads", $"node-v{version}-win-x64.zip");
            Download($"https://nodejs.org/dist/v{version}/node-v{version}-win-x64.zip", archive);
            RequireHash(archive, settings.GetProperty("node_windows_zip_sha256").GetString()!);
            ZipFile.ExtractToDirectory(archive, environment.Tools, overwriteFiles: true);
        }
        RequireVersion(node, ["--version"], "v" + version, exact: true, root);
        var source = Path.Combine(root, "tools", "Penrose");
        var target = Path.Combine(environment.Tools, "penrose");
        Directory.CreateDirectory(target);
        var fingerprint = string.Join(":", new[] { "package.json", "package-lock.json" }
            .Select(file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, file)))))) + ":" + version;
        var marker = Path.Combine(target, ".installed-lock");
        if (!File.Exists(marker) || File.ReadAllText(marker) != fingerprint || !File.Exists(environment.PenroseCli) ||
            !File.Exists(Path.Combine(target, "node_modules", "canvas", "build", "Release", "canvas.node")))
        {
            foreach (var file in new[] { "package.json", "package-lock.json" }) File.Copy(Path.Combine(source, file), Path.Combine(target, file), overwrite: true);
            var npm = Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js");
            Execute(node, [npm, "ci", "--prefix", target, "--legacy-peer-deps", "--no-audit", "--no-fund"], root, root);
        }
        using var installed = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "node_modules", "@penrose", "roger", "package.json")));
        if (installed.RootElement.GetProperty("version").GetString() != settings.GetProperty("roger_version").GetString())
            throw new InvalidDataException("Installed Penrose version does not match dependencies.lock.json");
        Execute(node, ["--input-type=module", "-e", "import 'global-jsdom/register'; if (!document.createElement('canvas').getContext('2d')) throw new Error('Penrose text measurement is unavailable');"], target, root, echo: false);
        File.WriteAllText(marker, fingerprint);
        Console.WriteLine("Penrose roger " + settings.GetProperty("roger_version").GetString());
    }

    private static void Download(string url, string path)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Console.WriteLine("Downloading " + Path.GetFileName(path));
        var partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using (var file = File.Create(partial)) response.Content.CopyToAsync(file).GetAwaiter().GetResult();
            File.Move(partial, path);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private static void RequireHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Checksum mismatch: {path}");
    }

    private static void RequireVersion(string exe, string[] args, string expected, bool exact, string root)
    {
        var actual = Execute(exe, args, root, root, echo: false).Trim();
        if (exact ? actual != expected : !actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected tool version: {exe}: {actual}");
        Console.WriteLine(actual);
    }

    private static string Execute(string executable, string[] arguments, string directory, string root, bool clearLocale = false, bool echo = true)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        new LocalToolchain(root).Configure(info);
        if (clearLocale)
            foreach (var key in new[] { "LC_ALL", "LC_CTYPE", "LC_COLLATE", "LC_MONETARY", "LC_TIME" }) info.Environment.Remove(key);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (echo) Console.Write(output);
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with {process.ExitCode}: {output}");
        return output;
    }

}

internal static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public nint File;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustData
    {
        public uint Size;
        public nint PolicyCallback;
        public nint SipClient;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref TrustData data);

    public static void RequireTrusted(string path)
    {
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = System.IO.Path.GetFullPath(path) };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        Marshal.StructureToPtr(file, pointer, false);
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer, StateAction = 1 };
        try
        {
            var status = WinVerifyTrust(0, ref action, ref data);
            if (status != 0) throw new InvalidDataException($"Installer Authenticode verification failed: 0x{status:X8}: {path}");
        }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(0, ref action, ref data);
            Marshal.DestroyStructure<FileInfo>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }
}
