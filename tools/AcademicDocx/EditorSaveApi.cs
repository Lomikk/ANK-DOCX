using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using AcademicDocx.Core;

namespace AcademicDocx;

// The browser can save only one of the already discovered diagrams of this
// session's project. No client-supplied filesystem destinations are accepted.
internal sealed class EditorSaveApi : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task loop;
    private readonly string root;
    private readonly string project;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string Address { get; }

    internal EditorSaveApi(string root, string project)
    {
        this.root = root; this.project = project;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Address = "http://127.0.0.1:" + port;
        listener.Prefixes.Add(Address + "/");
        listener.Start();
        loop = Task.Run(Listen);
    }

    private async Task Listen()
    {
        while (!cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(cancellation.Token); }
            catch (Exception e) when (e is OperationCanceledException or HttpListenerException or ObjectDisposedException) { break; }
            try { await Handle(context); }
            catch (Exception e)
            {
                try { await Reply(context, 400, new { error = e.Message }); }
                catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException) { }
            }
            finally { context.Response.Close(); }
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var origin = request.Headers["Origin"];
        if (request.Headers["X-Ank-Session"] != Token ||
            origin is not (null or "http://127.0.0.1:3000" or "http://localhost:3000"))
        { await Reply(context, 403, new { error = "Недопустимая сессия редактора" }); return; }
        var diagrams = PenroseDiagram.Find(project);
        if (request.HttpMethod == "GET" && request.Url!.AbsolutePath == "/ank/diagrams")
        {
            await Reply(context, 200, new { project = Path.GetFileName(project), diagrams = diagrams.Select(d =>
            {
                using var trio = JsonDocument.Parse(File.ReadAllText(d.Trio));
                var substance = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(d.Trio)!, trio.RootElement.GetProperty("substance").GetString()!));
                return new { id = d.Id, substance = Path.GetRelativePath(Path.Combine(project, "figures", "penrose"), substance).Replace('\\', '/') };
            }).ToArray() });
            return;
        }
        if (request.HttpMethod != "POST" || request.Url!.AbsolutePath != "/ank/save")
        { await Reply(context, 404, new { error = "Неизвестная команда" }); return; }
        if (request.ContentLength64 is <= 0 or > 20_000_000) throw new InvalidDataException("Недопустимый размер рисунка");
        var save = await JsonSerializer.DeserializeAsync<SaveRequest>(request.InputStream, Json) ?? throw new InvalidDataException("Пустой запрос");
        var diagram = diagrams.SingleOrDefault(d => d.Id == save.Id) ?? throw new InvalidDataException("Выберите рисунок этой работы");
        if (save.Domain is null || save.Substance is null || save.Style is null || save.Variation is null || save.Svg is null)
            throw new InvalidDataException("Нужны исходники и построенный SVG");
        diagram.SaveToWorkspace(new PenroseSource(save.Domain, save.Substance, save.Style, save.Variation, save.Svg), (trio, svg) =>
        {
            var folder = Path.GetDirectoryName(svg)!;
            PenroseCommands.Render(root, new PenroseDiagram(project, diagram.Id, trio), Path.Combine(folder, "validation.svg"));
            PenroseCommands.Normalize(root, project, svg, Path.Combine(folder, "word.svg"));
        });
        await Reply(context, 200, new { saved = true, diagram = diagram.Id, message = "SVG и Trio сохранены в работу. Можно собирать DOCX." });
    }

    private static async Task Reply(HttpListenerContext context, int status, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        cancellation.Cancel(); listener.Close();
        loop.GetAwaiter().GetResult(); cancellation.Dispose();
    }

    private sealed record SaveRequest(string Id, string Domain, string Substance, string Style, string Variation, string Svg);
}
