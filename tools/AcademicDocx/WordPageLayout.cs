using AcademicDocx.Core;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

internal sealed record PageLayoutReport(string Standard, int FirstStampedPage, int StampTotalPages);

internal static class WordPageLayout
{
    public static PageLayoutReport? Verify(WordSession session, PageLayoutOptions? options, int pages, int? annotationPage)
    {
        if (options?.Technical != true) return null;
        var scope = session.Objects;
        var fields = WordFields.InPageDrawings(session);
        var totals = fields.Where(f => scope.Own(f.Code).Text.Trim() == "NUMPAGES").ToArray();
        if (totals.Length == 0 || totals.Any(f => !int.TryParse(scope.Own(f.Result).Text.Trim(), out var value) || value != pages))
            throw new InvalidDataException("STO 04 stamp NUMPAGES is missing or does not match final pagination");
        Word.Section? firstSection = null;
        foreach (Word.Section item in scope.Own(session.Document!.Sections))
        {
            var section = scope.Own(item);
            if (WordTables.Page(scope, scope.Own(section.Range)) == annotationPage) { firstSection = section; break; }
        }
        if (firstSection is null) throw new InvalidDataException("STO 04 annotation must begin a page-layout section");
        var headers = scope.Own(firstSection.Headers);
        var first = scope.Own(headers[Word.WdHeaderFooterIndex.wdHeaderFooterFirstPage]);
        var firstFields = WordFields.InShapes(session, scope.Own(first.Shapes));
        var firstPage = WordTables.Page(scope, scope.Own(firstSection.Range));
        // PAGE in a repeating drawing is evaluated in each rendered page's
        // context. Its single COM cache is not the value of every occurrence.
        if (!firstFields.Any(f => scope.Own(f.Code).Text.Trim() == "PAGE") || firstPage != annotationPage)
            throw new InvalidDataException("STO 04 needs a PAGE field in the first header at the annotation section");
        return new(options.Standard, firstPage, pages);
    }
}
