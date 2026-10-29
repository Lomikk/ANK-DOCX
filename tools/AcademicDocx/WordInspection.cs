using AcademicDocx.Core;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

internal sealed record FieldSnapshot(string Code, string Result);
internal sealed record SourceSnapshot(string Tag, string Title);
internal sealed record WordSnapshot(string Label, IReadOnlyList<FieldSnapshot> Fields,
    IReadOnlyList<SourceSnapshot> Sources, IReadOnlyDictionary<string, string> Bookmarks,
    int Pages, int Equations, int TableMath, int Toc, IReadOnlyList<float> TableTextFontSizes,
    IReadOnlyList<float> TableMathFontSizes);
internal sealed record PdfExportReport(bool Exported, bool Closed, bool Passed, string? Error, WordLifecycle? Word);
internal sealed record WordInspectionReport(WordSnapshot Snapshot, WordLifecycle Word);

internal sealed class WordCheckReport
{
    public bool Passed { get; set; }
    public Dictionary<string, bool> Checks { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<WordSnapshot> Snapshots { get; } = [];
    public int ExpectedPages { get; set; }
    public string? TableMutationTarget { get; set; }
    public WordLifecycle? Word { get; set; }
    public string? Error { get; set; }
}

internal static class WordInspection
{
    public static WordInspectionReport Inspect(string path)
    {
        using var session = new WordSession();
        session.Open(path, readOnly: true);
        var snapshot = Snapshot(session, "opened");
        session.Close();
        session.RequireCleanExit();
        return new(snapshot, session.Lifecycle);
    }

    public static WordSnapshot Snapshot(WordSession session, string label)
    {
        var document = session.Document!;
        var scope = session.Objects;
        var fields = new List<FieldSnapshot>();
        foreach (Word.Field item in scope.Own(document.Fields))
        {
            var field = scope.Own(item);
            fields.Add(new(scope.Own(field.Code).Text.Trim(), scope.Own(field.Result).Text.Trim()));
        }
        var sources = new List<SourceSnapshot>();
        foreach (Word.Source item in scope.Own(scope.Own(document.Bibliography).Sources))
        {
            var source = scope.Own(item);
            sources.Add(new(source.Tag, source.Field["Title"]));
        }
        var bookmarks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Word.Bookmark item in scope.Own(document.Bookmarks))
        {
            var bookmark = scope.Own(item);
            if (!new[] { "tbl_", "fig_", "eq_", "sec_" }.Any(prefix => bookmark.Name.StartsWith(prefix, StringComparison.Ordinal))) continue;
            var range = scope.Own(bookmark.Range);
            scope.Own(range.TextRetrievalMode).IncludeFieldCodes = false;
            var text = range.Text.Trim();
            bookmarks[bookmark.Name] = text.Length <= 256 ? text : text[..256] + "…";
        }
        var tableMath = 0;
        var tableFontSizes = new List<float>();
        var tableMathFontSizes = new List<float>();
        foreach (Word.Table item in scope.Own(document.Tables))
        {
            var range = scope.Own(scope.Own(item).Range);
            foreach (Word.Paragraph paragraphItem in scope.Own(range.Paragraphs))
            {
                var textRange = scope.Own(scope.Own(scope.Own(paragraphItem).Range).Duplicate);
                var text = textRange.Text;
                textRange.End -= text.Length - text.TrimEnd('\r', '\a').Length;
                if (textRange.End > textRange.Start) tableFontSizes.Add(scope.Own(textRange.Font).Size);
            }
            var equations = scope.Own(range.OMaths);
            tableMath += equations.Count;
            foreach (Word.OMath equation in equations)
                tableMathFontSizes.Add(scope.Own(scope.Own(scope.Own(equation).Range).Font).Size);
        }
        return new(label, fields, sources, bookmarks, document.ComputeStatistics(Word.WdStatistic.wdStatisticPages),
            scope.Own(document.OMaths).Count, tableMath, scope.Own(document.TablesOfContents).Count,
            tableFontSizes.Distinct().ToArray(), tableMathFontSizes.Distinct().ToArray());
    }

    public static WordCheckReport Check(string input, string copy, int? expectedPages)
    {
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Word checks require a separate output copy");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(copy))!);
        File.Copy(input, copy, true);
        var expected = DocxInspection.Read(input);
        var report = new WordCheckReport();
        WordSession? session = null;
        try
        {
            session = new();
            var document = session.Open(copy);
            var scope = session.Objects;
            var before = Snapshot(session, "opened");
            report.Snapshots.Add(before);
            if (expected.SourceTags.Count > 0)
                report.Checks["native_sources_recognized"] = expected.SourceTags.Order().SequenceEqual(before.Sources.Select(s => s.Tag).Order());
            else
                report.Skipped.Add("Native Word bibliography sources: the document uses Citeproc bibliography entries");
            report.Checks["native_math_recognized"] = expected.MathCount == before.Equations &&
                expected.Tables.Sum(table => table.MathCount) == before.TableMath;
            report.Checks["table_base_font_size_11pt"] = before.TableTextFontSizes.All(size => size == 11) &&
                before.TableMathFontSizes.All(size => size == 11);
            report.Checks["native_toc_recognized"] = !expected.Fields.Any(f => f.StartsWith("TOC ", StringComparison.Ordinal)) || before.Toc > 0;
            report.ExpectedPages = expectedPages ?? before.Pages;
            var numPages = before.Fields.Where(f => f.Code == "NUMPAGES").ToArray();
            report.Checks["page_count_current"] = numPages.All(f => int.TryParse(f.Result, out var value) && value == report.ExpectedPages);

            var mathEdited = false;
            foreach (Word.Table tableItem in scope.Own(document.Tables))
            {
                foreach (Word.OMath equationItem in scope.Own(scope.Own(scope.Own(tableItem).Range).OMaths))
                {
                    var equation = scope.Own(equationItem);
                    foreach (Word.OMathFunction functionItem in scope.Own(equation.Functions))
                    {
                        var function = scope.Own(functionItem);
                        if (function.Type != Word.WdOMathFunctionType.wdOMathFunctionScrSup) continue;
                        var sup = scope.Own(scope.Own(scope.Own(function.ScrSup).Sup).Range);
                        if (sup.Text.Trim() != "2") continue;
                        sup.Text = "3";
                        equation.BuildUp();
                        mathEdited = true;
                        break;
                    }
                    if (mathEdited) break;
                }
                if (mathEdited) break;
            }
            if (mathEdited) report.Checks["cell_math_edited_in_word"] = true;
            else report.Skipped.Add("Cell exponent edit: no superscript 2 in table math");

            var bookmarks = scope.Own(document.Bookmarks);
            string? tableName = null;
            string? sequence = null;
            foreach (var name in before.Bookmarks.Keys.Where(name => name.StartsWith("tbl_", StringComparison.Ordinal))
                .OrderBy(name => scope.Own(scope.Own(bookmarks[name]).Range).Start))
            {
                foreach (Word.Field fieldItem in scope.Own(scope.Own(scope.Own(bookmarks[name]).Range).Fields))
                {
                    var code = scope.Own(scope.Own(fieldItem).Code).Text.Trim();
                    if (!code.StartsWith("SEQ ", StringComparison.Ordinal)) continue;
                    tableName = name;
                    sequence = code;
                    break;
                }
                if (tableName is not null) break;
            }
            if (tableName is not null)
            {
                report.TableMutationTarget = tableName;
                var paragraph = scope.Own(scope.Own(scope.Own(scope.Own(bookmarks[tableName]).Range).Paragraphs)[1]);
                var at = scope.Own(document.Range(scope.Own(paragraph.Range).Start, scope.Own(paragraph.Range).Start));
                at.InsertBefore("Additional table [ACADEMIC_CHECK_NUMBER]\r");
                var findRange = scope.Own(document.Content);
                if (!scope.Own(findRange.Find).Execute("[ACADEMIC_CHECK_NUMBER]", MatchWildcards: false, Format: false,
                    Forward: true, Wrap: Word.WdFindWrap.wdFindStop)) throw new InvalidOperationException("Cannot locate inserted test marker");
                findRange.Text = "";
                scope.Own(scope.Own(document.Fields).Add(findRange, Word.WdFieldType.wdFieldEmpty, sequence!, false));
            }
            else report.Skipped.Add("Sequence mutation: document has no numbered semantic table bookmark");

            var sectionName = before.Bookmarks.Keys.FirstOrDefault(name => name.StartsWith("sec_", StringComparison.Ordinal));
            if (sectionName is not null && before.Toc > 0)
            {
                var heading = scope.Own(scope.Own(scope.Own(scope.Own(bookmarks[sectionName]).Range).Paragraphs)[1]);
                var range = scope.Own(heading.Range);
                scope.Own(document.Range(range.End - 1, range.End - 1)).InsertAfter(" [academic check]");
            }
            else report.Skipped.Add("Heading mutation: no semantic section bookmark or TOC");

            WordFields.Refresh(session);
            var after = Snapshot(session, "edited");
            report.Snapshots.Add(after);
            if (tableName is not null)
            {
                if (!int.TryParse(before.Bookmarks[tableName], out var initialNumber)) throw new InvalidDataException("Table bookmark has no numeric SEQ result");
                report.Checks["table_number_updated"] = after.Bookmarks[tableName] == (initialNumber + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var references = after.Fields.Where(field => field.Code.Split(' ', StringSplitOptions.RemoveEmptyEntries) is var tokens &&
                    tokens.Length >= 2 && tokens[0] == "REF" && tokens[1].StartsWith("tbl_", StringComparison.Ordinal)).ToArray();
                if (references.Length > 0) report.Checks["table_references_updated"] = references.All(field =>
                {
                    var target = field.Code.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    return after.Bookmarks.TryGetValue(target, out var number) && field.Result == number;
                });
                if (references.Length == 0) report.Skipped.Add("Reference update: document has no table REF fields");
            }
            if (sectionName is not null && before.Toc > 0)
            {
                var toc = scope.Own(scope.Own(document.TablesOfContents)[1]);
                var range = scope.Own(toc.Range);
                scope.Own(range.TextRetrievalMode).IncludeFieldCodes = false;
                report.Checks["heading_updated_in_toc"] = range.Text.Contains("[academic check]", StringComparison.Ordinal);
            }
            document.Save();
            session.Close();
            session.Open(copy, readOnly: true);
            var reopened = Snapshot(session, "reopened");
            report.Snapshots.Add(reopened);
            report.Checks["saved_closed_reopened"] = after.Equations == reopened.Equations && after.TableMath == reopened.TableMath &&
                (tableName is null || after.Bookmarks[tableName] == reopened.Bookmarks[tableName]);
            session.Close();
            session.RequireCleanExit();
            report.Checks["sdk_schema_valid"] = DocxInspection.Validate(copy).ErrorCount == 0;
            report.Passed = report.Checks.Values.All(value => value);
        }
        catch (Exception error) { report.Error = error.ToString(); }
        finally
        {
            session?.Dispose();
            report.Word = session?.Lifecycle;
            if (report.Word?.CleanupErrors.Count > 0) report.Passed = false;
        }
        return report;
    }

    public static PdfExportReport ExportPdf(string input, string pdf)
    {
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(pdf), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("PDF output must differ from DOCX input");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pdf))!);
        WordSession? session = null;
        var exported = false;
        var closed = false;
        string? error = null;
        try
        {
            session = new();
            var document = session.Open(input, readOnly: true);
            document.ExportAsFixedFormat(Path.GetFullPath(pdf), Word.WdExportFormat.wdExportFormatPDF);
            exported = File.Exists(pdf) && new FileInfo(pdf).Length > 0;
            session.Close();
            closed = true;
            session.RequireCleanExit();
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { session?.Dispose(); }
        return new(exported, closed, exported && closed && error is null && session?.Lifecycle.CleanupErrors.Count == 0, error, session?.Lifecycle);
    }
}
