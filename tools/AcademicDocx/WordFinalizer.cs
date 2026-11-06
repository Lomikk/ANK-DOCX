using System.Diagnostics;
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
    public PageLayoutReport? PageLayout { get; set; }
    public List<TableSplitReport> SplitTables { get; } = [];
    public WordLifecycle? Word { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, double> Seconds { get; } = [];
}

internal static class WordFields
{
    public static IReadOnlyList<Word.Field> InShapes(WordSession session, Word.Shapes shapes)
    {
        var fields = new List<Word.Field>();
        var scope = session.Objects;
        void Visit(Word.Shape shape)
        {
            if ((int)shape.Type == 6) // Office msoGroup; recurse through native Word group shapes.
            {
                var items = scope.Own(shape.GroupItems);
                for (var i = 1; i <= items.Count; i++) Visit(scope.Own(items[i]));
                return;
            }
            var frame = scope.Own(shape.TextFrame);
            if (frame.HasText == 0) return;
            foreach (Word.Field field in scope.Own(scope.Own(frame.TextRange).Fields)) fields.Add(scope.Own(field));
        }
        foreach (Word.Shape shape in shapes) Visit(scope.Own(shape));
        return fields;
    }

    public static IReadOnlyList<Word.Field> InPageDrawings(WordSession session)
    {
        var fields = new List<Word.Field>();
        var scope = session.Objects;
        foreach (Word.Section item in scope.Own(session.Document!.Sections))
        {
            var section = scope.Own(item);
            foreach (var stories in new[] { scope.Own(section.Headers), scope.Own(section.Footers) })
                foreach (Word.HeaderFooter storyItem in stories)
                {
                    var story = scope.Own(storyItem);
                    if (story.Exists) fields.AddRange(InShapes(session, scope.Own(story.Shapes)));
                }
        }
        return fields;
    }
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
                var tocFields = scope.Own(scope.Own(toc.Range).Fields);
                if (tocFields.Count > 0 && scope.Own(scope.Own(tocFields[1]).Code).Text.Contains("\\f A", StringComparison.Ordinal))
                {
                    toc.UseFields = true;
                    toc.TableID = "A";
                }
                if (rebuildToc) toc.Update(); else toc.UpdatePageNumbers();
            }
            foreach (Word.TableOfFigures item in scope.Own(document.TablesOfFigures))
            {
                var figures = scope.Own(item);
                if (rebuildToc) figures.Update(); else figures.UpdatePageNumbers();
            }
            foreach (var field in InPageDrawings(session))
                if (!field.Update()) throw new InvalidOperationException("Word page-drawing field update failed");
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
        fields.AddRange(InPageDrawings(session).Where(field => scope.Own(field.Code).Text.Trim() == "NUMPAGES"));
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
    public static FinalizationReport Run(string path, PageLayoutOptions? layout = null, string? templates = null,
        Action<BuildProgressUpdate>? progress = null)
    {
        path = Path.GetFullPath(path);
        var report = new FinalizationReport { File = path };
        var staged = Path.Combine(Path.GetDirectoryName(path)!, $".academic-{Guid.NewGuid():N}.docx");
        WordSession? session = null;
        var timer = Stopwatch.StartNew();
        var stage = "";
        void Step(string next, string message)
        {
            if (stage.Length != 0) report.Seconds[stage] = timer.Elapsed.TotalSeconds;
            stage = next; timer.Restart();
            progress?.Invoke(new(next, message));
        }
        try
        {
            Step("prepare", "Подготовка оформления DOCX");
            report.SchemaErrorsBeforeWord = DocxInspection.Validate(path).ErrorCount;
            var original = DocxInspection.Read(path);
            File.Copy(path, staged);
            DocumentFormatting.Apply(staged);
            AppendixFormatting.Apply(staged, layout);
            report.Annotation = FrontMatterFormatting.Apply(staged);
            AuthorSignatureFormatting.Apply(staged);
            DocumentPageLayout.Apply(staged, layout, templates);
            Step("word-open", "Открытие документа в Word");
            session = new();
            var document = session.Open(staged);
            Step("front-matter", "Компоновка сопроводительных листов");
            WordAppendices.MarkContents(session, layout);
            WordFrontMatter.Layout(session);
            Step("fields", "Обновление оглавления и ссылок");
            WordFields.Refresh(session);
            Step("tables", "Размещение таблиц по страницам");
            var parts = WordTables.Split(session, (completed, total, name) =>
                progress?.Invoke(new("tables", name, completed, total)));
            var tables = session.Objects.Own(document.Tables);
            var tableIndices = new Dictionary<int, int>();
            if (parts.Count > 0)
                for (var index = 1; index <= tables.Count; index++)
                    tableIndices.Add(session.Objects.Own(session.Objects.Own(tables[index]).Range).Start, index);
            foreach (var table in parts)
            {
                var indices = new List<int>();
                foreach (var part in table.Parts)
                {
                    if (tableIndices.TryGetValue(part.Start, out var index)) indices.Add(index);
                }
                if (indices.Count != table.Parts.Count) throw new InvalidDataException("Cannot locate saved table fragments");
                report.SplitTables.Add(new(table.Name, indices));
            }
            Step("normalize", "Сохранение и нормализация Word");
            document.Save();
            session.Close();
            document = session.Open(staged);
            Step("fields-final", "Обновление окончательных номеров страниц");
            WordFields.Refresh(session, rebuildToc: false);
            Step("verify", "Проверка страниц и частей таблиц");
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
            WordAppendices.VerifyContents(session, layout);
            report.FrontMatter = WordFrontMatter.Verify(session, report.Annotation);
            report.PageLayout = WordPageLayout.Verify(session, layout, report.Pages.Value, report.FrontMatter?.AnnotationStart);
            Step("save", "Сохранение итогового документа");
            document.Save();
            session.Close();
            session.RequireCleanExit();
            Step("validation", "Проверка структуры DOCX");
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
            if (stage.Length != 0) report.Seconds[stage] = timer.Elapsed.TotalSeconds;
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
