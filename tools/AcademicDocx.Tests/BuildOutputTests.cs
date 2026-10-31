using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AcademicDocx.Tests;

public sealed class BuildOutputTests
{
    [WordFact]
    public void BuildOverwritesBothJournalsAndOnlyVerboseStreamsTheFullProcess()
    {
        var source = Environment.GetEnvironmentVariable("ACADEMIC_TEMPLATE_SOURCE")!;
        var repository = Path.GetFullPath(Path.Combine(source, ".."));
        var root = Path.Combine(Path.GetTempPath(), "ANK logs " + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Workspaces", "Проверка лога");
        var extension = Path.Combine(project, "_extensions", "academic");
        Directory.CreateDirectory(extension);
        Directory.CreateDirectory(Path.Combine(project, "chapters"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        File.Copy(Path.Combine(repository, "dependencies.lock.json"), Path.Combine(root, "dependencies.lock.json"));
        using var dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dependencies.lock.json")));
        var version = dependencies.RootElement.GetProperty("quarto").GetProperty("version").GetString();
        // A junction reuses the real pinned CLI; the fixture format needs no R or Word.
        var link = Path.Combine(root, ".tools", "quarto-" + version);
        var target = Path.Combine(repository, ".tools", "quarto-" + version);
        var rLink = Path.Combine(root, ".tools", "R");
        foreach (var (from, to) in new[] { (link, target), (rLink, Path.Combine(repository, ".tools", "R")) })
        {
            var junction = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/d", "/c", "mklink", "/J", from, to }) junction.ArgumentList.Add(arg);
            using var process = Process.Start(junction)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        File.WriteAllText(Path.Combine(extension, "_extension.yml"), "title: Fixture\ncontributes:\n  formats:\n    docx: default\n");
        var configuration = Path.Combine(project, "_quarto.yml");
        var yaml = "project:\n  type: default\n  render: [chapters/main.qmd]\n  output-dir: _output\nformat: academic-docx\nbibliography: references.bib\n";
        File.WriteAllText(Path.Combine(project, "references.bib"), "");
        File.WriteAllText(configuration, yaml);
        var input = Path.Combine(project, "chapters", "main.qmd");
        var summary = Path.Combine(project, ".quarto", "build-summary.log");
        var full = Path.Combine(project, ".quarto", "build.log");
        try
        {
            File.WriteAllText(input, "---\ntitle: Logging fixture\n---\nSee @fig-missing. Citation [@missing-source].\n");
            var warning = Run(root, project);
            Assert.True(warning.Code == 0, warning.Output + File.ReadAllText(full));
            Assert.Equal(warning.Output, File.ReadAllText(summary));
            Assert.Contains("@fig-missing", warning.Output);
            Assert.Contains("missing-source", warning.Output);
            Assert.DoesNotContain("metadata", warning.Output);
            Assert.Contains("metadata", File.ReadAllText(full));

            File.Copy(Path.Combine(source, "_extensions", "academic", "content-advice.lua"), Path.Combine(extension, "content-advice.lua"));
            File.WriteAllText(Path.Combine(extension, "_extension.yml"), "title: Fixture\ncontributes:\n  formats:\n    docx:\n      filters: [content-advice.lua]\n");
            File.WriteAllText(input, """
                # Глава

                <!-- comment does not count -->

                ## Подраздел

                Упомянута другая таблица [-@tbl-other].

                | A | B |
                |---|---|
                | 1 | 2 |

                : Данные {#tbl-data}

                Упоминание после объекта [-@tbl-data].

                # Без подраздела

                Текст.
                """);
            var advice = Run(root, project);
            Assert.Equal(0, advice.Code);
            Assert.Equal(advice.Output, File.ReadAllText(summary));
            Assert.Contains("раздел «Глава»", advice.Output);
            Assert.Contains("таблицей tbl-data", advice.Output);
            Assert.DoesNotContain("раздел «Без подраздела»", advice.Output);

            File.WriteAllText(input, """
                # Глава

                Вводный текст.

                ## Подраздел

                Результат приведён в таблице [-@tbl-data].

                | A | B |
                |---|---|
                | 1 | 2 |

                : Данные {#tbl-data}

                # Глава со списком

                - Содержательный пункт.

                ## Подраздел списка

                # Глава с цитатой

                > Содержательная цитата.

                ## Подраздел цитаты
                """);
            var fixedAdvice = Run(root, project);
            Assert.Equal(0, fixedAdvice.Code);
            Assert.Equal(fixedAdvice.Output, File.ReadAllText(summary));
            Assert.DoesNotContain("Рекомендация:", fixedAdvice.Output + File.ReadAllText(full));
            Assert.Contains("не обнаружено", fixedAdvice.Output);

            File.WriteAllText(Path.Combine(project, "chapters", "_included.qmd"), """
                # Подключённый раздел

                ## Подраздел

                Без упоминания таблицы.

                | A | B |
                |---|---|
                | 1 | 2 |

                : Данные {#tbl-included}
                """);
            File.WriteAllText(input, "{{< include _included.qmd >}}\n");
            var includedAdvice = Run(root, project);
            Assert.Equal(0, includedAdvice.Code);
            Assert.Equal(includedAdvice.Output, File.ReadAllText(summary));
            Assert.Contains("раздел «Подключённый раздел»", includedAdvice.Output);
            Assert.Contains("таблицей tbl-included", includedAdvice.Output);

            File.WriteAllText(input, """
                # Объекты

                Параметр задаёт формула [-@eq-good].

                $$
                x=1
                $$ {#eq-good}

                В листинге [-@lst-good] приведён код.

                ```{#lst-good .r lst-cap="Рисунок 88 в подписи"}
                # Таблица 77 в коде
                x <- 1
                ```

                Таблица 3, рисунке 2.1, листинге 5, формуле (4).

                Ссылка на другой объект [-@eq-good].

                $$
                y=2
                $$ {#eq-missing}

                Упоминание после формулы [-@eq-missing].

                Листинг без вводной ссылки.

                ```{#lst-missing .r lst-cap="Листинг 44 в подписи"}
                x <- 2
                ```

                Текст `рисунок 80` и значение $x=42$.
                """);
            var objects = Run(root, project);
            Assert.Equal(0, objects.Code);
            Assert.Equal(objects.Output, File.ReadAllText(summary));
            Assert.Contains("найдено «Таблица 3»", objects.Output);
            Assert.Contains("рисунке 2.1", objects.Output);
            Assert.Contains("листинге 5", objects.Output);
            Assert.Contains("формуле (4)", objects.Output);
            Assert.Contains("формулой eq-missing", objects.Output);
            Assert.Contains("листингом lst-missing", objects.Output);
            Assert.DoesNotContain("формулой eq-good", objects.Output);
            Assert.DoesNotContain("листингом lst-good", objects.Output);
            Assert.DoesNotContain("рисунок 80", objects.Output);
            Assert.DoesNotContain("Рисунок 88", objects.Output);
            Assert.DoesNotContain("Таблица 77", objects.Output);

            File.WriteAllText(input, """
                # Объекты

                Зависимость представлена формулой [-@eq-missing].

                $$
                y=2
                $$ {#eq-missing}

                Алгоритм приведён в листинге [-@lst-missing].

                ```{#lst-missing .r lst-cap="Код"}
                x <- 2
                ```
                """);
            var fixedObjects = Run(root, project);
            Assert.Equal(0, fixedObjects.Code);
            Assert.Equal(fixedObjects.Output, File.ReadAllText(summary));
            Assert.DoesNotContain("Рекомендация:", fixedObjects.Output + File.ReadAllText(full));
            Assert.Contains("не обнаружено", fixedObjects.Output);
            File.WriteAllText(Path.Combine(extension, "_extension.yml"), "title: Fixture\ncontributes:\n  formats:\n    docx: default\n");

            // The old warning and an obsolete failed Word report must disappear.
            File.WriteAllText(Path.Combine(project, ".quarto", "main.docx.finalize.json"), "{\"error\":\"stale Word failure\"}");
            File.WriteAllText(input, "# Clean\nNo warnings.\n");
            var clean = Run(root, project);
            Assert.Equal(0, clean.Code);
            Assert.Equal(clean.Output, File.ReadAllText(summary));
            Assert.DoesNotContain("@fig-missing", clean.Output + File.ReadAllText(full));
            Assert.DoesNotContain("stale Word failure", clean.Output);
            Assert.Contains("не обнаружено", clean.Output);

            File.WriteAllText(input, "---\ntitle: [broken\n---\nText\n");
            var failure = Run(root, project);
            Assert.NotEqual(0, failure.Code);
            Assert.Equal(failure.Output, File.ReadAllText(summary));
            Assert.Contains("Ошибка сборки", failure.Output);
            Assert.DoesNotContain("не обнаружено", failure.Output);
            Assert.DoesNotContain("stale Word failure", failure.Output);
            Assert.Contains("Полный журнал", failure.Output);

            File.WriteAllText(input, "# Lua failure\nText\n");
            foreach (var file in new[] { "academic.lua", "front-matter.lua", "profile.lua", "profiles.json" })
                File.Copy(Path.Combine(source, "_extensions", "academic", file), Path.Combine(extension, file));
            File.WriteAllText(Path.Combine(extension, "_extension.yml"), "title: Fixture\ncontributes:\n  formats:\n    docx:\n      filters: [academic.lua]\n");
            var lua = Run(root, project);
            Assert.NotEqual(0, lua.Code);
            Assert.Equal(lua.Output, File.ReadAllText(summary));
            Assert.Contains("academic.profile", lua.Output);
            Assert.DoesNotContain("stack traceback:", lua.Output);
            File.WriteAllText(configuration, yaml);
            File.WriteAllText(Path.Combine(extension, "_extension.yml"), "title: Fixture\ncontributes:\n  formats:\n    docx: default\n");

            File.WriteAllText(input, "# Debug\nText\n");
            var verbose = Run(root, project, verbose: true);
            Assert.Equal(0, verbose.Code);
            Assert.Contains("pandoc", verbose.Output);
            Assert.DoesNotContain("pandoc", File.ReadAllText(summary));
            Assert.DoesNotContain("Ошибка сборки", File.ReadAllText(summary));
        }
        finally
        {
            Directory.Delete(link); // Remove the junction, never the shared tool installation.
            Directory.Delete(rLink);
            if (!Path.GetFullPath(root).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cleanup escapes TEMP");
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int Code, string Output) Run(string root, string project, bool verbose = false)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ACADEMIC_DOCX_SERVICE")!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "workspace", root, "build", Path.GetFileName(project) }) info.ArgumentList.Add(arg);
        if (verbose) info.ArgumentList.Add("--verbose");
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Build fixture timed out"); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
