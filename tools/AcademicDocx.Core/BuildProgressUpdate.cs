using System.Text.Json;

namespace AcademicDocx.Core;

/// <summary>Operational progress from the Word worker; never a diagnostic.</summary>
public sealed record BuildProgressUpdate(string Stage, string Message, int Completed = 0, int Total = 0)
{
    public const string Prefix = "ANK-PROGRESS ";
    public string ToLine() => Prefix + JsonSerializer.Serialize(this);
    public static BuildProgressUpdate? Read(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            var update = JsonSerializer.Deserialize<BuildProgressUpdate>(line[Prefix.Length..]);
            return update is { Stage.Length: > 0, Message.Length: > 0, Completed: >= 0, Total: >= 0 } ? update : null;
        }
        catch (JsonException) { return null; }
    }
}
