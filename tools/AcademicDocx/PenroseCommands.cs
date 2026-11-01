using System.Diagnostics;
using System.Net;
using System.Text;
using AcademicDocx.Core;

namespace AcademicDocx;

internal static class PenroseCommands
{
    internal static PenroseDiagram[] Diagrams(string root, string name)
    {
        var project = WorkspaceCommands.Project(root, name);
        if (!File.Exists(Path.Combine(project, "_quarto.yml"))) throw new DirectoryNotFoundException("Работа не найдена: " + project);
        return PenroseDiagram.Find(project);
    }

    internal static void Run(string root, string name, string[] args)
    {
        var diagrams = Diagrams(root, name);
        if (args is ["setup"]) { PenroseEditor.Install(root); return; }
        if (args is ["list"])
        {
            foreach (var d in diagrams) Console.WriteLine(d.Id + (d.HasEdits ? " — сохранённый SVG" : " — автоматический"));
            return;
        }
        if (diagrams.Length == 0) throw new FileNotFoundException("В figures/penrose/ этой работы нет файлов .trio.json.");
        if (args is ["open"] or ["serve"])
        {
            using var editor = PenroseEditor.Start(root, diagrams[0].Project, openBrowser: args[0] == "open");
            Console.WriteLine("Enter — остановить редактор и вернуться в CMD.");
            Console.ReadLine();
            return;
        }
        if (args is ["prepare"])
        {
            var catalog = Prepare(root, diagrams);
            Console.WriteLine("Каталог: " + catalog);
            return;
        }
        if (args.Length < 2) throw new ArgumentException("figures NAME list | open | prepare | apply ID SVG | generate ID");
        var diagram = diagrams.SingleOrDefault(d => d.Id.Equals(args[1].Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Рисунок не найден. Посмотрите figures NAME list.");
        if (args is ["apply", _, var svg])
        {
            svg = Path.GetFullPath(svg);
            _ = PenroseDiagram.ReadSvg(svg);
            // Render before touching the current edit/history; a malformed SVG is not applied.
            var check = Path.Combine(diagram.Project, ".quarto", "penrose-editor", Guid.NewGuid() + ".svg");
            try { Normalize(root, diagram.Project, svg, check); diagram.Apply(svg); }
            finally { File.Delete(check); }
            Console.WriteLine("Применён рисунок: " + diagram.Edited + "\nОбычная сборка сохранит эти правки. Если сохранённая версия уже существовала, она скопирована в .history рядом с рисунком.");
        }
        else if (args is ["generate", _])
        {
            var candidate = Path.Combine(diagram.Project, ".quarto", "penrose-editor", "generated", diagram.Id + ".svg");
            // Failed generation leaves the edited source in place. Only success switches back.
            diagram.UseAutomatic(() =>
            {
                Render(root, diagram, candidate);
                Normalize(root, diagram.Project, candidate, diagram.Output);
            });
            Console.WriteLine("Сгенерирован автоматический вариант: " + diagram.Output + "\nПрежний сохранённый SVG находится в .history, если он существовал.");
        }
        else throw new ArgumentException("figures NAME list | open | prepare | apply ID SVG | generate ID");
    }

    private static string Prepare(string root, PenroseDiagram[] diagrams)
    {
        var folder = Path.Combine(diagrams[0].Project, ".quarto", "penrose-editor");
        Directory.CreateDirectory(folder);
        var html = new StringBuilder("""
            <!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Рисунки Penrose — ANK DOCX</title>
            <style>body{font:17px/1.5 system-ui;background:#f5f5f2;color:#202823;margin:32px auto;padding:0 24px;max-width:1200px}h1{font-size:30px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:24px}article{background:white;border:1px solid #d4dbd5;border-radius:12px;padding:24px}img{width:100%;height:390px;object-fit:contain}a{color:#15623f}h2{overflow-wrap:anywhere}code{background:#eceeea;padding:2px 5px}p{max-width:950px}</style>
            <h1>Рисунки Penrose</h1>
            <p>1. Скачайте SVG нужного рисунка. 2. Запустите редактор через меню ANK DOCX и <a href="http://127.0.0.1:3000/try/" target="_blank" rel="noopener">откройте локальный Penrose</a>; загрузите файл через Import / Upload.
            3. В Settings включите Interactive Mode → Edit Mode. После правок экспортируйте <b>Penrose SVG</b>.
            4. В меню ANK DOCX выберите «Применить SVG из редактора», затем соберите работу.</p>
            <p>Сохранённый SVG используется при обычных сборках. Экспортированный файл содержит изображение и исходники; повторное открытие в редакторе заново компилирует исходники, поэтому прежние перемещения мышью могут не восстановиться. Не все вычисляемые элементы доступны для перетаскивания.</p><main>
            """);
        foreach (var d in diagrams)
        {
            var file = Path.Combine(folder, "files", d.Id.Replace('/', Path.DirectorySeparatorChar) + ".svg");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (d.HasEdits) File.Copy(d.Edited, file, overwrite: true);
            else Render(root, d, file);
            d.AttachEditorSources(file);
            var url = "files/" + string.Join('/', d.Id.Split('/').Select(Uri.EscapeDataString)) + ".svg";
            var label = WebUtility.HtmlEncode(d.Id);
            html.Append($"<article><h2>{label}</h2><p>{(d.HasEdits ? "Сохранённый пользователем SVG" : "Автоматический вариант")}</p><img src=\"{url}\" alt=\"{label}\"><p><a href=\"{url}\" download>Скачать SVG для редактора</a></p></article>");
        }
        html.Append("</main></html>");
        var index = Path.Combine(folder, "index.html");
        File.WriteAllText(index, html.ToString(), new UTF8Encoding(false));
        return index;
    }

    internal static void Render(string root, PenroseDiagram diagram, string output)
    {
        var tools = new LocalToolchain(root);
        if (tools.Node is null || !File.Exists(tools.Node) || !File.Exists(tools.PenroseCli))
            throw new FileNotFoundException("Penrose не установлен. Запустите ANK-DOCX.cmd setup.");
        Tool(root, diagram.Project, tools.Node, [tools.PenroseCli, "trio", "--trio", diagram.Trio, "--out", output], output);
    }

    internal static void Normalize(string root, string project, string source, string output) =>
        Tool(root, project, new LocalToolchain(root).Svg, ["--format", "svg", "--output", output, source], output);

    private static void Tool(string root, string project, string executable, string[] args, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        new LocalToolchain(root).Configure(start);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new TimeoutException("Не завершилось построение SVG."); }
        var log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidOperationException("Ошибка обработки рисунка: " + log);
    }
}
