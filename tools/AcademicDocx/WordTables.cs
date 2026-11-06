using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

internal sealed record WordRow(int Index, int Start, int End);
internal sealed record SplitTable(string Name, IReadOnlyList<Word.Range> Parts);
internal sealed record TableSplitReport(string Name, IReadOnlyList<int> TableIndices);
internal sealed record TableTarget(string Name, Word.Paragraph Caption, Word.Table Table, int Index);

internal static class WordTables
{
    public static int Page(ComScope scope, Word.Range range)
    {
        var point = scope.Own(range.Duplicate);
        point.Collapse(Word.WdCollapseDirection.wdCollapseStart);
        return (int)point.Information[Word.WdInformation.wdActiveEndPageNumber];
    }

    private static IReadOnlyList<WordRow> Rows(ComScope scope, Word.Table table)
    {
        // Word.Rows cannot enumerate vertically merged header cells. Cell
        // RowIndex and range positions still describe the body boundaries.
        var rows = new SortedDictionary<int, WordRow>();
        foreach (Word.Cell item in scope.Own(scope.Own(table.Range).Cells))
        {
            var cell = scope.Own(item);
            var range = scope.Own(cell.Range);
            var index = cell.RowIndex;
            rows[index] = rows.TryGetValue(index, out var previous)
                ? previous with { End = Math.Max(previous.End, range.End) }
                : new(index, range.Start, range.End);
        }
        return rows.Values.ToArray();
    }

    public static IReadOnlyList<SplitTable> Split(WordSession session, Action<int, int, string>? progress = null)
    {
        var document = session.Document!;
        var scope = session.Objects;
        var bookmarks = scope.Own(document.Bookmarks);
        var targets = new List<(string Name, int Start)>();
        foreach (Word.Bookmark item in bookmarks)
        {
            var bookmark = scope.Own(item);
            if (!bookmark.Name.StartsWith("tbl_", StringComparison.Ordinal)) continue;
            var range = scope.Own(bookmark.Range);
            var sequences = 0;
            foreach (Word.Field fieldItem in scope.Own(range.Fields))
            {
                var field = scope.Own(fieldItem);
                if (scope.Own(field.Code).Text.Trim().StartsWith("SEQ ", StringComparison.Ordinal)) sequences++;
            }
            if (sequences == 1) targets.Add((bookmark.Name, range.Start));
        }
        // Index native tables once instead of repeatedly enumerating the entire
        // suffix of the document for every caption. COM objects remain live as
        // preceding tables are split and their range positions change.
        var nativeTables = scope.Own(document.Tables);
        var indexed = new List<(Word.Table Table, int Start, int Index)>();
        for (var index = 1; index <= nativeTables.Count; index++)
        {
            var table = scope.Own(nativeTables[index]);
            indexed.Add((table, scope.Own(table.Range).Start, index));
        }
        var prepared = new List<TableTarget>();
        var cursor = 0;
        foreach (var target in targets.OrderBy(target => target.Start))
        {
            progress?.Invoke(prepared.Count, targets.Count, "Подготовка ширины таблиц");
            var bookmark = scope.Own(bookmarks[target.Name]);
            var caption = scope.Own(scope.Own(scope.Own(bookmark.Range).Paragraphs)[1]);
            var captionRange = scope.Own(caption.Range);
            while (cursor < indexed.Count && indexed[cursor].Start < captionRange.End) cursor++;
            if (cursor == indexed.Count) throw new InvalidDataException($"Missing table after caption {target.Name}");
            var current = indexed[cursor].Table;
            var tableRange = scope.Own(current.Range);
            if (!string.IsNullOrWhiteSpace(scope.Own(document.Range(captionRange.End, tableRange.Start)).Text))
                throw new InvalidDataException($"Table caption {target.Name} is not adjacent to its table");
            if (current.PreferredWidthType == Word.WdPreferredWidthType.wdPreferredWidthAuto && current.AllowAutoFit)
            {
                var rows = scope.Own(current.Rows);
                rows.Alignment = Word.WdRowAlignment.wdAlignRowLeft;
                rows.LeftIndent = 0;
                current.AutoFitBehavior(Word.WdAutoFitBehavior.wdAutoFitWindow);
                var section = scope.Own(scope.Own(tableRange.Sections)[1]);
                var pageSetup = scope.Own(section.PageSetup);
                current.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPoints;
                current.PreferredWidth = pageSetup.PageWidth - pageSetup.LeftMargin - pageSetup.RightMargin;
            }
            scope.Own(current.Rows).AllowBreakAcrossPages = 0;
            prepared.Add(new(target.Name, caption, current, indexed[cursor].Index));
        }
        var result = new List<SplitTable>();
        var inserted = 0;
        var completed = 0;
        // Width and row rules must be applied before measuring pages, but a
        // short, unchanged table never needs another whole-document pass.
        var needsPagination = true;
        foreach (var target in prepared)
        {
            progress?.Invoke(completed++, targets.Count, "Размещение таблиц по страницам");
            var caption = target.Caption;
            var current = target.Table;
            var currentIndex = target.Index + inserted;
            var tableRange = scope.Own(current.Range);
            if (needsPagination) { document.Repaginate(); needsPagination = false; }
            var end = scope.Own(tableRange.Duplicate);
            end.End--;
            end.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
            if (Page(scope, tableRange) == Page(scope, end)) continue;

            // WordOpenXML is the public Flat OPC range representation, not a
            // source-text parser. Only detect unsupported table structures.
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var xml = XDocument.Parse(tableRange.WordOpenXML);
            var xmlTable = xml.Descendants(w + "body").Elements(w + "tbl").First();
            var xmlRows = xmlTable.Elements(w + "tr").ToArray();
            var headerCount = xmlRows.TakeWhile(row =>
            {
                var header = row.Element(w + "trPr")?.Element(w + "tblHeader");
                return header is not null && (string?)header.Attribute(w + "val") is not ("0" or "false" or "off");
            }).Count();
            if (headerCount == 0) throw new InvalidDataException($"Long table {target.Name} needs a repeating header");
            foreach (var row in xmlRows.Skip(headerCount))
            {
                if (row.Descendants(w + "vMerge").Any())
                    throw new InvalidDataException($"Long table {target.Name} has vertically merged body cells; automatic splitting is not supported");
                if (row.Descendants(w + "tbl").Any())
                    throw new InvalidDataException($"Nested tables in long table {target.Name} are not supported");
            }
            current.AutoFitBehavior(Word.WdAutoFitBehavior.wdAutoFitFixed);
            var firstBody = Rows(scope, current).FirstOrDefault(row => row.Index > headerCount)
                ?? throw new InvalidDataException($"No body rows in long table {target.Name}");
            var headerRange = scope.Own(document.Range(scope.Own(current.Range).Start, firstBody.Start));
            var captions = new List<Word.Range>();
            var parts = new List<Word.Range>();
            var done = false;
            for (var pass = 0; pass < xmlRows.Length; pass++)
            {
                document.Repaginate();
                var body = Rows(scope, current).Where(row => row.Index > headerCount).ToArray();
                if (body.Length == 0) throw new InvalidDataException($"Empty fragment in {target.Name}");
                var page = Page(scope, scope.Own(document.Range(body[0].Start, body[0].Start)));
                WordRow? split = null;
                foreach (var row in body)
                {
                    var startPage = Page(scope, scope.Own(document.Range(row.Start, row.Start)));
                    var endPage = Page(scope, scope.Own(document.Range(row.End - 2, row.End - 2)));
                    if (startPage != endPage)
                        throw new InvalidDataException($"A body row in {target.Name} crosses pages; reduce the row or revise its grouping");
                    if (startPage > page) { split = row; break; }
                }
                if (split is null)
                {
                    parts.Add(scope.Own(scope.Own(current.Range).Duplicate));
                    done = true;
                    break;
                }
                var tables = scope.Own(document.Tables);
                scope.Own(document.Range(split.Start, split.Start)).Select();
                scope.Own(session.Application.Selection).SplitTable();
                parts.Add(scope.Own(scope.Own(current.Range).Duplicate));
                currentIndex++; inserted++;
                var tail = scope.Own(tables[currentIndex]);
                var at = scope.Own(scope.Own(tail.Range).Duplicate);
                at.Collapse(Word.WdCollapseDirection.wdCollapseStart);
                at.FormattedText = scope.Own(headerRange.FormattedText);
                tail = scope.Own(tables[currentIndex]);
                var start = scope.Own(tail.Range).Start;
                var paragraph = scope.Own(scope.Own(scope.Own(document.Range(start - 1, start - 1)).Paragraphs)[1]);
                var paragraphRange = scope.Own(paragraph.Range);
                // Keep the paragraph mark; deleting it joins the two tables.
                paragraphRange.InsertBefore("Продолжение таблицы ");
                paragraph.set_Style(scope.Own((Word.Style)caption.get_Style()));
                var format = scope.Own(paragraphRange.ParagraphFormat);
                format.Alignment = Word.WdParagraphAlignment.wdAlignParagraphLeft;
                format.LeftIndent = 0;
                format.RightIndent = 0;
                format.FirstLineIndent = 0;
                format.PageBreakBefore = -1;
                // This caption always starts a page; no outer gap above it.
                format.SpaceBefore = 0;
                format.KeepWithNext = -1;
                at = scope.Own(paragraphRange.Duplicate);
                at.End--;
                at.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                scope.Own(scope.Own(document.Fields).Add(at, Word.WdFieldType.wdFieldRef, target.Name + " \\h", false));
                captions.Add(scope.Own(paragraphRange.Duplicate));
                current = tail;
            }
            if (!done) throw new InvalidDataException($"Table splitting did not converge for {target.Name}");
            if (captions.Count > 0)
            {
                var last = scope.Own(captions[^1].Duplicate);
                last.End = last.Start + "Продолжение".Length;
                last.Text = "Окончание";
            }
            result.Add(new(target.Name, parts));
            needsPagination = true;
        }
        progress?.Invoke(targets.Count, targets.Count, "Таблицы размещены");
        return result;
    }
}
