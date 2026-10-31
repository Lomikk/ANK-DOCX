using AcademicDocx.Core;

namespace AcademicDocx;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--version"])
            {
                Console.WriteLine(typeof(Program).Assembly.GetName().Version!.ToString(3));
                return 0;
            }
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine("""
                    Academic DOCX service
                      --version
                      finalize-word DOCX [--report JSON] [--layout-metadata JSON] [--page-templates DIRECTORY]
                      validate DOCX [DOCX ...]
                      style-reference DOCX [--output DOCX] [--bottom-margin-mm 20|26]
                      check-word DOCX COPY REPORT [--expected-pages N]
                      export-pdf DOCX PDF REPORT
                      inspect-word DOCX REPORT
                      setup REPOSITORY_ROOT [--development]
                      package-service REPOSITORY_ROOT PUBLICATION_DIRECTORY
                      quarto-path REPOSITORY_ROOT
                      prepare-sto04 TEACHER_DOCX OUTPUT_DIRECTORY
                      workspace REPOSITORY_ROOT [new NAME [--profile PROFILE] | build NAME | list | profiles]
                    """);
                return 0;
            }
            switch (args[0])
            {
                case "quarto-path":
                    if (args.Length != 2) throw new ArgumentException("quarto-path REPOSITORY_ROOT");
                    Console.WriteLine(QuartoLauncher.Executable(args[1]));
                    return 0;
                case "package-service":
                    if (args.Length != 3) throw new ArgumentException("package-service REPOSITORY_ROOT PUBLICATION_DIRECTORY");
                    ServiceDistribution.Package(args[1], args[2]);
                    return 0;
                case "prepare-sto04":
                    if (args.Length != 3) throw new ArgumentException("prepare-sto04 TEACHER_DOCX OUTPUT_DIRECTORY");
                    TechnicalPageTemplates.Prepare(args[1], args[2]);
                    return 0;
                case "workspace":
                    Require(args, 2);
                    WorkspaceCommands.Run(args[1], args.Skip(2).ToArray());
                    return 0;
                case "inspect-word":
                {
                    if (args.Length != 3) throw new ArgumentException("inspect-word DOCX REPORT");
                    RequireDistinctPaths(args[1], args[2]);
                    var report = WordInspection.Inspect(args[1]);
                    JsonReport.Write(args[2], report);
                    Console.Write(JsonReport.Serialize(new { Report = Path.GetFullPath(args[2]), report.Snapshot.Pages, report.Word }));
                    return 0;
                }
                case "setup":
                    if (args.Length != 2 && !(args.Length == 3 && args[2] == "--development")) throw new ArgumentException("setup REPOSITORY_ROOT [--development]");
                    EnvironmentSetup.Run(args[1], args.Length == 3);
                    return 0;
                case "check-word":
                {
                    Require(args, 4);
                    CheckOptions(args, 4, "--expected-pages");
                    var expected = Option(args, "--expected-pages");
                    RequireDistinctPaths(args[1], args[2], args[3]);
                    var report = WordInspection.Check(args[1], args[2], expected is null ? null : int.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
                    JsonReport.Write(args[3], report);
                    Console.Write(JsonReport.Serialize(new { report.Passed, report.Checks, report.Skipped, report.TableMutationTarget, report.Word, report.Error, Report = Path.GetFullPath(args[3]) }));
                    return report.Passed ? 0 : 1;
                }
                case "export-pdf":
                {
                    if (args.Length != 4) throw new ArgumentException("export-pdf DOCX PDF REPORT");
                    RequireDistinctPaths(args[1], args[2], args[3]);
                    var report = WordInspection.ExportPdf(args[1], args[2]);
                    JsonReport.Write(args[3], report);
                    Console.Write(JsonReport.Serialize(report));
                    return report.Passed ? 0 : 1;
                }
                case "finalize-word":
                {
                    Require(args, 2);
                    CheckOptions(args, 2, "--report", "--layout-metadata", "--page-templates");
                    if (Option(args, "--report") is { } reportPath) RequireDistinctPaths(args[1], reportPath);
                    var layoutFile = Option(args, "--layout-metadata");
                    var report = WordFinalizer.Run(args[1], layoutFile is null ? null : PageLayoutOptions.Read(layoutFile), Option(args, "--page-templates"));
                    if (Option(args, "--report") is { } output) JsonReport.Write(output, report);
                    Console.Write(JsonReport.Serialize(report));
                    return report.Passed ? 0 : 1;
                }
                case "validate":
                {
                    Require(args, 2);
                    var reports = args.Skip(1).Select(DocxInspection.Validate).ToArray();
                    Console.Write(JsonReport.Serialize(reports));
                    return reports.All(report => report.ErrorCount == 0) ? 0 : 1;
                }
                case "style-reference":
                {
                    Require(args, 2);
                    CheckOptions(args, 2, "--output", "--bottom-margin-mm");
                    var output = Option(args, "--output") ?? args[1];
                    var bottom = int.Parse(Option(args, "--bottom-margin-mm") ?? "26", System.Globalization.CultureInfo.InvariantCulture);
                    ReferenceStyles.Apply(args[1], output, bottom);
                    var report = DocxInspection.Validate(output);
                    Console.Write(JsonReport.Serialize(report));
                    return report.ErrorCount == 0 ? 0 : 1;
                }
                default: throw new ArgumentException($"Unknown command: {args[0]}. Use help.");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static void Require(string[] args, int count)
    {
        if (args.Length < count) throw new ArgumentException("Missing arguments. Use help.");
    }

    private static void RequireDistinctPaths(params string[] paths)
    {
        if (paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("Input, output copy/PDF, and JSON report must have distinct paths");
    }

    internal static string? Option(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index < 0 ? null : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing value for {option}");
    }

    internal static void CheckOptions(string[] args, int start, params string[] names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = start; i < args.Length; i += 2)
        {
            if (!names.Contains(args[i], StringComparer.Ordinal) || !seen.Add(args[i]) || i + 1 >= args.Length)
                throw new ArgumentException($"Invalid, repeated, or incomplete option: {args[i]}");
        }
    }
}
