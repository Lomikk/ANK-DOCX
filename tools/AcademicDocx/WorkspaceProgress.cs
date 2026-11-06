using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using AcademicDocx.Core;
using Spectre.Console;

namespace AcademicDocx;

// Spectre owns terminal animation. Worker callbacks only update the state;
// the waiting thread alone touches the live display.
internal sealed class WorkspaceProgress
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly string history;
    private readonly double previous;
    private readonly bool verbose;
    private readonly object gate = new();
    private BuildProgressUpdate current = new("quarto", "Подготовка сборки");
    private int revision;
    private TimeSpan batchStarted;

    public WorkspaceProgress(string folder, bool verbose)
    {
        this.verbose = verbose;
        history = Path.Combine(folder, "build-duration.txt");
        try
        {
            if (File.Exists(history) && double.TryParse(File.ReadAllText(history), CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0)
                previous = value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public bool Observe(string line)
    {
        var update = BuildProgressUpdate.Read(line);
        var operational = update is not null;
        if (update is null)
        {
            if (line.Contains("processing file:", StringComparison.Ordinal)) update = new("execute", "Расчёты и подготовка таблиц");
            else if (Regex.Match(line, @"^\s*(\d+)/(\d+)(?:\s|$)") is { Success: true } steps &&
                int.TryParse(steps.Groups[1].Value, out var done) && int.TryParse(steps.Groups[2].Value, out var total) && total > 0 && done <= total)
                update = new("execute", "Расчёты и подготовка таблиц", done, total);
            else if (Regex.Match(line, @"^\s*(\d+)%\s*\[") is { Success: true } percent)
                update = new("execute", "Расчёты и подготовка таблиц: " + percent.Groups[1].Value + "%");
            else if (line.StartsWith("pandoc ", StringComparison.Ordinal)) update = new("pandoc", "Создание документа DOCX");
            else if (line.StartsWith("Running script ", StringComparison.Ordinal) && line.Contains("finalize", StringComparison.Ordinal))
                update = new("hook", "Подготовка нумерации и объектов Word");
        }
        if (update is not null) lock (gate)
        {
            if (update.Total != current.Total || update.Message != current.Message || update.Completed < current.Completed)
                batchStarted = clock.Elapsed;
            current = update; revision++;
        }
        return operational;
    }

    private string Message()
    {
        lock (gate)
        {
            var count = current.Total > 0 ? $" ({current.Completed}/{current.Total})" : "";
            var elapsed = clock.Elapsed;
            var batchElapsed = elapsed - batchStarted;
            var estimate = previous > elapsed.TotalSeconds
                ? "; осталось примерно " + Duration(TimeSpan.FromSeconds(previous - elapsed.TotalSeconds)) + " по прошлой сборке"
                : previous > 0 ? "; дольше прошлой сборки"
                : current.Completed >= 3 && current.Total > current.Completed && batchElapsed.TotalSeconds >= 2
                    ? "; для этапа осталось примерно " + Duration(batchElapsed * ((double)current.Total / current.Completed - 1))
                    : "; общее время оценивается после первой успешной сборки";
            return current.Message + count + " — прошло " + Duration(elapsed) + estimate;
        }
    }

    public void Wait(Process process)
    {
        if (!verbose && !Console.IsOutputRedirected && !Console.IsErrorRedirected && AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start(Markup.Escape(Message()), context =>
            {
                while (!process.WaitForExit(200)) context.Status(Markup.Escape(Message()));
            });
        }
        else
        {
            var shown = -1;
            var last = TimeSpan.FromSeconds(-10);
            do
            {
                int change; lock (gate) change = revision;
                // In redirected logs, avoid hundreds of per-cell/per-table lines.
                if ((change != shown && clock.Elapsed - last >= TimeSpan.FromSeconds(2)) || clock.Elapsed - last >= TimeSpan.FromSeconds(10))
                {
                    Console.Error.WriteLine("Сборка: " + Message());
                    shown = change; last = clock.Elapsed;
                }
            } while (!process.WaitForExit(200));
        }
        process.WaitForExit(); // Drain asynchronous stdout/stderr callbacks.
    }

    public void Finish(bool success)
    {
        if (success)
        {
            try { File.WriteAllText(history, clock.Elapsed.TotalSeconds.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        Console.Error.WriteLine((success ? "Сборка завершена за " : "Сборка остановлена через ") + Duration(clock.Elapsed) + ".");
    }

    private static string Duration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}
