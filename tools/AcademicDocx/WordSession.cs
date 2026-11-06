using System.Diagnostics;
using System.Runtime.InteropServices;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

// Own only COM objects created by this worker. Never attach to the user's Word
// and never terminate Word processes by name.
internal sealed class ComScope : IDisposable
{
    private readonly List<object> owned = [];
    private readonly HashSet<object> seen = new(ReferenceEqualityComparer.Instance);

    public T Own<T>(T value) where T : class
    {
        if (seen.Add(value)) owned.Add(value);
        return value;
    }

    public void Dispose()
    {
        for (var index = owned.Count - 1; index >= 0; index--)
            if (Marshal.IsComObject(owned[index])) Marshal.FinalReleaseComObject(owned[index]);
        owned.Clear();
        seen.Clear();
    }
}

internal sealed record WordLifecycle(string Version, string Build, int ProcessId, bool ProcessExited, IReadOnlyList<string> CleanupErrors);

internal sealed class WordSession : IDisposable
{
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    private readonly ComScope applicationScope = new();
    private bool disposed;
    private int processId;
    private bool processExited;
    private readonly List<string> cleanupErrors = [];
    public Word.Application Application { get; }
    public ComScope Objects { get; private set; } = new();
    public Word.Document? Document { get; private set; }
    private readonly string version;
    private readonly string build;
    public WordLifecycle Lifecycle => new(version, build, processId, processExited, cleanupErrors);

    public WordSession()
    {
        Application = applicationScope.Own(new Word.Application());
        try
        {
            Application.Visible = false;
            Application.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            version = Application.Version;
            build = Application.Build;
            Application.ScreenUpdating = false;
        }
        catch
        {
            try { Application.Quit(Word.WdSaveOptions.wdDoNotSaveChanges); } finally { applicationScope.Dispose(); }
            throw;
        }
    }

    public Word.Document Open(string path, bool readOnly = false)
    {
        if (Document is not null) throw new InvalidOperationException("Close the previous document first");
        var documents = Objects.Own(Application.Documents);
        Document = Objects.Own(documents.Open(Path.GetFullPath(path), ReadOnly: readOnly, AddToRecentFiles: false));
        var window = Objects.Own(Document.ActiveWindow);
        GetWindowThreadProcessId((nint)window.Hwnd, out var pid);
        if (processId != 0 && processId != (int)pid) throw new InvalidOperationException("Word changed its owning process");
        processId = (int)pid;
        var view = Objects.Own(window.View);
        view.Type = Word.WdViewType.wdPrintView;
        // Pagination must use field results, independently of the saved/user
        // code-display preference. Change only this worker's document view.
        view.ShowFieldCodes = false;
        return Document;
    }

    public void Close()
    {
        Document?.Close(Word.WdSaveOptions.wdDoNotSaveChanges);
        Document = null;
        Objects.Dispose();
        Objects = new();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Close(); } catch (Exception error) { cleanupErrors.Add("Close: " + error.Message); }
        try { Application.ScreenUpdating = true; } catch (Exception error) { cleanupErrors.Add("ScreenUpdating: " + error.Message); }
        try { Application.Quit(Word.WdSaveOptions.wdDoNotSaveChanges); } catch (Exception error) { cleanupErrors.Add("Quit: " + error.Message); }
        Objects.Dispose();
        applicationScope.Dispose();
        // Release temporary COM enumerators after all explicitly owned RCWs.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (processId != 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                processExited = process.WaitForExit(5000);
                if (!processExited) cleanupErrors.Add("The worker's Word process did not exit");
            }
            catch (ArgumentException) { processExited = true; }
        }
    }

    public void RequireCleanExit()
    {
        Dispose();
        if (cleanupErrors.Count > 0) throw new InvalidOperationException(string.Join("; ", cleanupErrors));
    }
}
