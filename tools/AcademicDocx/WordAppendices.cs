using AcademicDocx.Core;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

internal static class WordAppendices
{
    public static void MarkContents(WordSession session, PageLayoutOptions? options)
    {
        if (options?.AppendixLayout != "research" || options.Appendices is not { Count: > 0 } appendices) return;
        var scope = session.Objects;
        var document = session.Document!;
        var fields = scope.Own(document.Fields);
        for (var i = fields.Count; i >= 1; i--)
        {
            var field = scope.Own(fields[i]);
            if (field.Type == Word.WdFieldType.wdFieldTOCEntry && scope.Own(field.Code).Text.TrimStart().StartsWith("TC \"ПРИЛОЖЕНИЕ ", StringComparison.Ordinal)) field.Delete();
        }
        var bookmarks = scope.Own(document.Bookmarks);
        foreach (var appendix in appendices)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(appendix.Id, "[^A-Za-z0-9_]", "_");
            var range = scope.Own(scope.Own(bookmarks[name]).Range);
            range.Collapse(Word.WdCollapseDirection.wdCollapseStart);
            scope.Own(scope.Own(document.TablesOfContents).MarkEntry(range,
                Entry: "ПРИЛОЖЕНИЕ " + appendix.Letter + ". " + appendix.Title, TableID: "A", Level: "1"));
        }
    }

    public static void VerifyContents(WordSession session, PageLayoutOptions? options)
    {
        if (options?.AppendixLayout != "research" || options.Appendices is not { Count: > 0 } appendices) return;
        var scope = session.Objects;
        foreach (Word.TableOfContents item in scope.Own(session.Document!.TablesOfContents))
        {
            var text = scope.Own(scope.Own(item).Range).Text;
            foreach (var appendix in appendices)
                if (!text.Contains("ПРИЛОЖЕНИЕ " + appendix.Letter + ". " + appendix.Title, StringComparison.Ordinal))
                    throw new InvalidDataException("Word contents omitted appendix: " + appendix.Id);
        }
    }
}
