using System.Text.RegularExpressions;

namespace AcademicDocx.Core;

/// <summary>Select existing tool diagnostics from complete captured process lines.</summary>
public static class BuildDiagnostics
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);
    private static readonly Regex Diagnostic = new(@"^\[?(?:ERROR\b|WARN(?:ING)?\b|Error\b|Warning\b|Ошибка\b|Предупреждени[ея]\b|Рекомендаци[яи]\b|Recommendation\b|Quitting from\b)", RegexOptions.CultureInvariant);
    private static readonly Regex ErrorHeader = new(@"^\[?(?:ERROR|Error|Ошибка)\b", RegexOptions.CultureInvariant);
    private static readonly Regex Trace = new(@"^(?:\s*(?:stack traceback:|Stack trace:|Backtrace:|Calls:)|\s+(?:at|в) \S+\()", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Read(string path)
    {
        var messages = new List<string>();
        var errors = new List<string>();
        if (!File.Exists(path)) return messages;
        var block = new List<string>();
        void Flush()
        {
            var message = Compact(string.Join(Environment.NewLine, block));
            var target = ErrorHeader.IsMatch(message.TrimStart()) ? errors : messages;
            if (message.Length != 0 && !target.Contains(message, StringComparer.Ordinal)) target.Add(message);
            block.Clear();
        }
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        while (reader.ReadLine() is { } raw)
        {
            var line = Ansi.Replace(raw, "");
            if (line.StartsWith(BuildProgressUpdate.Prefix, StringComparison.Ordinal)) { Flush(); continue; }
            var trimmed = line.Trim();
            if (Diagnostic.IsMatch(trimmed))
            {
                Flush();
                if (trimmed is not ("WARN: Error encountered when rendering files" or "Error encountered when rendering files"))
                    block.Add(line);
            }
            else if (block.Count != 0)
            {
                if (trimmed.Length == 0 || trimmed.StartsWith("Output created:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("Running script ", StringComparison.Ordinal) ||
                    Trace.IsMatch(line)) Flush();
                else { block.Add(line); if (block.Count == 8) Flush(); }
            }
        }
        Flush();
        return errors.Concat(messages).ToArray();
    }

    public static string Compact(string text) => string.Join(Environment.NewLine,
        Ansi.Replace(text, "").Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .TakeWhile(line => !Trace.IsMatch(line))
            .Where(line => !string.IsNullOrWhiteSpace(line)).Take(8)
            .Select(line => line.Length > 1600 ? line[..1600] + "…" : line));
}
