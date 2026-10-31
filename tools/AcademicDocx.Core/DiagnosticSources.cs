using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcademicDocx.Core;

/// <summary>Use Quarto inspect's code-cell locations only when a label identifies one cell.</summary>
public static class DiagnosticSources
{
    private static readonly Regex Label = new(@"(?:\[(?<cell>[^\]\r\n]+)\]|(?:таблиц(?:ей|а)|рисунк(?:ом|а))\s+(?<cell>(?:tbl|fig)-[\w-]+))", RegexOptions.CultureInvariant);
    private static readonly Regex Quitting = new(@"^Quitting from .*\[[^\]]+\]", RegexOptions.CultureInvariant);

    private static bool Candidate(string line) => line.StartsWith("Quitting from ", StringComparison.Ordinal) ||
        line.StartsWith("Рекомендация:", StringComparison.Ordinal);
    public static bool NeedsInspection(IEnumerable<string> messages) => messages.Any(m =>
        m.Split(Environment.NewLine).Any(line => Candidate(line) && Label.IsMatch(line)));

    public static IReadOnlyList<string> AddLocations(IEnumerable<string> messages, string inspectJson, string project)
    {
        using var json = JsonDocument.Parse(inspectJson);
        var cells = new Dictionary<string, List<(string File, int Start, int End, string Code)>>(StringComparer.Ordinal);
        var hasIncludes = false;
        if (!json.RootElement.TryGetProperty("fileInformation", out var information) || information.ValueKind != JsonValueKind.Object)
            return messages.ToArray();
        foreach (var document in information.EnumerateObject())
        {
            if (document.Value.TryGetProperty("includeMap", out var includes) && includes.ValueKind == JsonValueKind.Array && includes.GetArrayLength() > 0)
                hasIncludes = true;
            if (!document.Value.TryGetProperty("codeCells", out var items) || items.ValueKind != JsonValueKind.Array) continue;
            foreach (var cell in items.EnumerateArray())
            {
                if (!cell.TryGetProperty("metadata", out var meta) || !meta.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String ||
                    !cell.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.String ||
                    !cell.TryGetProperty("start", out var start) || !start.TryGetInt32(out var first) ||
                    !cell.TryGetProperty("end", out var end) || !end.TryGetInt32(out var last) || first < 0 || last < first) continue;
                var id = label.GetString()!;
                var path = Path.GetFullPath(file.GetString()!, project);
                var relative = Path.GetRelativePath(project, path);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                var source = cell.TryGetProperty("source", out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()! : "";
                var code = source.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
                if (code.Length > 160) code = code[..160] + "…";
                if (!cells.TryGetValue(id, out var matches)) cells[id] = matches = [];
                matches.Add((relative.Replace('\\', '/'), first + 1, last + 1, code));
            }
        }
        return messages.Select(message => string.Join(Environment.NewLine, message.Split(Environment.NewLine).Select(line =>
        {
            if (!Candidate(line)) return line;
            var match = Label.Match(line);
            if (!match.Success || !cells.TryGetValue(match.Groups["cell"].Value, out var found) || found.Count != 1)
                return hasIncludes && Quitting.IsMatch(line)
                    ? line + " (строки объединённого документа; исходный файл не определён)" : line;
            var cell = found[0];
            var location = $"{cell.File}:{cell.Start}-{cell.End}";
            var snippet = cell.Code.Length == 0 ? "" : $"; начало кода: {cell.Code}";
            return Quitting.IsMatch(line) ? $"Quitting from {location} [{match.Groups["cell"].Value}]{snippet}" : $"{line} (ячейка: {location}{snippet})";
        }))).ToArray();
    }
}
