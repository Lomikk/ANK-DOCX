using AcademicDocx.Core;
using Word = Microsoft.Office.Interop.Word;

namespace AcademicDocx;

internal sealed record FrontMatterPages(int TitleEnd, int? AssignmentFirstEnd, int? AssignmentEnd, int AnnotationStart, int BodyBoundary);

internal static class WordFrontMatter
{
    public static void Layout(WordSession session, AnnotationStatistics? statistics)
    {
        if (statistics is null) return;
        var document = session.Document!;
        var scope = session.Objects;
        Word.Paragraph? title = null, signatures = null, place = null;
        foreach (Word.Paragraph item in scope.Own(document.Paragraphs))
        {
            var paragraph = scope.Own(item);
            var range = scope.Own(paragraph.Range);
            if (range.get_Style() is not Word.Style styleObject) continue;
            var style = scope.Own(styleObject).NameLocal;
            if (style is "AcademicCourseworkTitle" or "AcademicReferatTitle" or "AcademicResearchTitle") title = paragraph;
            if (title is not null && (style is "AcademicSignColumns" or "AcademicRightBlock" or "AcademicRightBlockStart") && signatures is null) signatures = paragraph;
            if (style == "AcademicTitlePlace") { place = paragraph; break; }
        }
        if (title is null || signatures is null || place is null) throw new InvalidDataException("Incomplete title form");
        var setup = scope.Own(scope.Own(scope.Own(document.Sections)[1]).PageSetup);
        document.Repaginate();
        Word.Paragraph? firstSignature = null, lastSignature = null;
        var fixedAssignment = false;
        foreach (Word.Paragraph item in scope.Own(document.Paragraphs))
        {
            var paragraph = scope.Own(item);
            var range = scope.Own(paragraph.Range);
            if (range.get_Style() is not Word.Style styleObject) continue;
            var style = scope.Own(styleObject).NameLocal;
            if (style == "AcademicAssignmentPageEnd") fixedAssignment = true;
            if (style == "AcademicFormSignature") firstSignature = paragraph;
            if (firstSignature is not null && style == "AcademicFormText") lastSignature = paragraph;
            if (style == "AcademicAssignmentEnd") break;
        }
        if (fixedAssignment && firstSignature is not null && lastSignature is not null)
        {
            var lastRange = scope.Own(lastSignature.Range);
            if (WordTables.Page(scope, lastRange) == 3)
            {
                var lastTop = Convert.ToSingle(lastRange.Information[Word.WdInformation.wdVerticalPositionRelativeToPage]);
                var gap = setup.PageHeight - setup.BottomMargin - 20 - lastTop;
                if (gap > 0) scope.Own(firstSignature.Format).SpaceBefore += gap;
                document.Repaginate();
            }
        }
        var rangePlace = scope.Own(place.Range);
        if (WordTables.Page(scope, rangePlace) != 1) return; // Verify reports overflow without hiding text.
        var top = Convert.ToSingle(rangePlace.Information[Word.WdInformation.wdVerticalPositionRelativeToPage]);
        var available = setup.PageHeight - setup.BottomMargin - 20 - top;
        if (available <= 0) return;
        // Word's public layout API provides the equivalent of the original
        // template's flexible vertical gaps, including wrapped institution names.
        foreach (var paragraph in new[] { title, signatures, place })
        {
            var format = scope.Own(paragraph.Format);
            format.SpaceBefore += available / 3;
        }
        document.Repaginate();
    }

    public static FrontMatterPages? Verify(WordSession session, AnnotationStatistics? statistics)
    {
        if (statistics is null) return null;
        var document = session.Document!;
        var scope = session.Objects;
        var expected = new Dictionary<string, int>
        {
            ["AcademicFigures"] = statistics.Figures, ["AcademicTables"] = statistics.Tables,
            ["AcademicSources"] = statistics.Sources, ["AcademicAppendices"] = statistics.Appendices
        };
        foreach (Word.Field item in scope.Own(document.Fields))
        {
            var field = scope.Own(item);
            var code = scope.Own(field.Code).Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (code.Length < 2 || code[0] != "DOCPROPERTY" || !expected.TryGetValue(code[1], out var count)) continue;
            if (!int.TryParse(scope.Own(field.Result).Text.Trim(), out var actual) || actual != count)
                throw new InvalidDataException($"Annotation field {code[1]} was not updated by Word");
            expected.Remove(code[1]);
        }
        if (expected.Count != 0) throw new InvalidDataException("Annotation is missing generated statistics fields");
        var pages = new Dictionary<string, int>();
        foreach (Word.Paragraph item in scope.Own(document.Paragraphs))
        {
            var paragraph = scope.Own(item);
            var range = scope.Own(paragraph.Range);
            if (range.get_Style() is not Word.Style styleObject) continue;
            var style = scope.Own(styleObject);
            var name = style.NameLocal;
            if (name is "AcademicTitleEnd" or "AcademicAssignmentPageEnd" or "AcademicAssignmentEnd" or "AcademicBodyStart")
                pages[name] = WordTables.Page(scope, range);
            if (name == "AcademicSummaryHeading" || name == "AcademicSheetHeading" && range.Text.Trim() == "АННОТАЦИЯ")
                pages["Annotation"] = WordTables.Page(scope, range);
            if (name == "AcademicBodyStart") break;
        }
        if (pages["AcademicTitleEnd"] != 1)
            throw new InvalidDataException("Title form exceeds one page; shorten the supplied fields or revise the supported form layout");
        if (pages.TryGetValue("AcademicAssignmentPageEnd", out var firstEnd) &&
            (firstEnd != 2 || pages["AcademicAssignmentEnd"] != 3))
            throw new InvalidDataException("Assignment exceeds its two-page form; revise the layout for this assignment before release");
        return new(pages["AcademicTitleEnd"], pages.GetValueOrDefault("AcademicAssignmentPageEnd") is 0 ? null : firstEnd,
            pages.GetValueOrDefault("AcademicAssignmentEnd") is 0 ? null : pages["AcademicAssignmentEnd"],
            pages["Annotation"], pages["AcademicBodyStart"]);
    }
}
