using AcademicDocx.Core;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

internal sealed class FinalizationReport
{
    public string File { get; init; } = "";
    public bool Passed { get; set; }
    public int? SchemaErrorsBeforeWord { get; set; }
    public ValidationReport? Validation { get; set; }
    public int? Pages { get; set; }
    public AnnotationStatistics? Annotation { get; set; }
    public FrontMatterPages? FrontMatter { get; set; }
    public List<TableSplitReport> SplitTables { get; } = [];
    public WordLifecycle? Word { get; set; }
    public string? Error { get; set; }
}

internal static class WordFields
{
    public static void Refresh(WordSession session, bool rebuildToc = true)
    {
        var document = session.Document!;
        var scope = session.Objects;
        for (var pass = 0; pass < 2; pass++)
        {
            document.Repaginate();
            if (rebuildToc)
            {
                foreach (Word.Range storyItem in scope.Own(document.StoryRanges))
                {
                    Word.Range? range = scope.Own(storyItem);
                    while (range is not null)
                    {
                        if (scope.Own(range.Fields).Update() != 0) throw new InvalidOperationException("Word field update failed");
                        range = range.NextStoryRange;
                        if (range is not null) scope.Own(range);
                    }
                }
            }
            else
            {
                foreach (Word.Field item in scope.Own(document.Fields))
                {
                    var field = scope.Own(item);
                    if (field.Type is Word.WdFieldType.wdFieldRef or Word.WdFieldType.wdFieldSequence)
                        if (!field.Update()) throw new InvalidOperationException("Word REF/SEQ update failed");
                }
            }
            foreach (Word.TableOfContents item in scope.Own(document.TablesOfContents))
            {
                var toc = scope.Own(item);
                if (rebuildToc) toc.Update(); else toc.UpdatePageNumbers();
            }
            foreach (Word.TableOfFigures item in scope.Own(document.TablesOfFigures))
            {
                var figures = scope.Own(item);
                if (rebuildToc) figures.Update(); else figures.UpdatePageNumbers();
            }
        }
    }

    public static int StabilizePages(WordSession session)
    {
        var document = session.Document!;
        var scope = session.Objects;
        document.Repaginate();
        var fields = new List<Word.Field>();
        foreach (Word.Field item in scope.Own(document.Fields))
        {
            var field = scope.Own(item);
            if (scope.Own(field.Code).Text.Trim() == "NUMPAGES") fields.Add(field);
        }
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var field in fields)
                if (!field.Update()) throw new InvalidOperationException("NUMPAGES update failed");
            document.Repaginate();
            var pages = document.ComputeStatistics(Word.WdStatistic.wdStatisticPages);
            if (fields.All(field => int.TryParse(scope.Own(field.Result).Text.Trim(), out var value) && value == pages) &&
                (int)scope.Own(document.Content).Information[Word.WdInformation.wdNumberOfPagesInDocument] == pages) return pages;
        }
        throw new InvalidOperationException("NUMPAGES and Word pagination did not stabilize");
    }
}

internal static class WordFinalizer
{
    public static FinalizationReport Run(string path)
    {
        path = Path.GetFullPath(path);
        var report = new FinalizationReport { File = path };
        var staged = Path.Combine(Path.GetDirectoryName(path)!, $".academic-{Guid.NewGuid():N}.docx");
        WordSession? session = null;
        try
        {
            report.SchemaErrorsBeforeWord = DocxInspection.Validate(path).ErrorCount;
            var original = DocxInspection.Read(path);
            File.Copy(path, staged);
            DocumentFormatting.Apply(staged);
            report.Annotation = FrontMatterFormatting.Apply(staged);
            session = new();
            var document = session.Open(staged);
            WordFrontMatter.Layout(session, report.Annotation);
            WordFields.Refresh(session);
            var parts = WordTables.Split(session);
            var tables = session.Objects.Own(document.Tables);
            foreach (var table in parts)
            {
                var indices = new List<int>();
                foreach (var part in table.Parts)
                {
                    for (var index = 1; index <= tables.Count; index++)
                    {
                        if (session.Objects.Own(session.Objects.Own(tables[index]).Range).Start != part.Start) continue;
                        indices.Add(index);
                        break;
                    }
                }
                if (indices.Count != table.Parts.Count) throw new InvalidDataException("Cannot locate saved table fragments");
                report.SplitTables.Add(new(table.Name, indices));
            }
            document.Save();
            session.Close();
            document = session.Open(staged);
            WordFields.Refresh(session, rebuildToc: false);
            foreach (var index in report.SplitTables.SelectMany(table => table.TableIndices))
            {
                var part = session.Objects.Own(session.Objects.Own(document.Tables)[index]);
                var range = session.Objects.Own(part.Range);
                var end = session.Objects.Own(range.Duplicate);
                end.End -= 2;
                end.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                if (WordTables.Page(session.Objects, range) != WordTables.Page(session.Objects, end))
                    throw new InvalidDataException($"Table fragment {index} spans pages after Word normalization");
            }
            report.Pages = WordFields.StabilizePages(session);
            report.FrontMatter = WordFrontMatter.Verify(session, report.Annotation);
            document.Save();
            session.Close();
            session.RequireCleanExit();
            report.Validation = DocxInspection.Validate(staged);
            if (report.Validation.ErrorCount > 0) throw new InvalidDataException("Word output failed Open XML SDK validation");
            var saved = DocxInspection.Read(staged);
            if (original.Fields.Any(field => field.StartsWith("TOC ", StringComparison.Ordinal)) &&
                !saved.Fields.Any(field => field.Contains("PAGEREF", StringComparison.Ordinal)))
                throw new InvalidDataException("Word did not save an updated TOC");
            File.Move(staged, path, overwrite: true);
            report.Passed = true;
        }
        catch (Exception error) { report.Error = error.ToString(); }
        finally
        {
            session?.Dispose();
            report.Word = session?.Lifecycle;
            if (report.Word?.CleanupErrors.Count > 0) report.Passed = false;
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (IOException error)
            {
                report.Error = (report.Error ?? "") + "\nTemporary file cleanup: " + error.Message;
                report.Passed = false;
            }
        }
        return report;
    }
}
