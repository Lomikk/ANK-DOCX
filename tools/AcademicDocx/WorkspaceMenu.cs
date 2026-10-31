using System.Diagnostics;
using Spectre.Console;

namespace AcademicDocx;

// The library owns keyboard navigation, pagination, terminal width and redraws.
internal static class WorkspaceMenu
{
    private sealed record Choice(string Id, string Label, string Description = "");
    private static bool Interactive => !Console.IsInputRedirected && !Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Interactive;

    public static void Run(string root)
    {
        Console.WriteLine("\nANK DOCX — работы в " + Path.Combine(root, "Workspaces"));
        while (true)
        {
            var action = Select("Что сделать?", [
                new("new", "Создать работу"), new("build", "Собрать DOCX"),
                new("folder", "Открыть папку работы"), new("document", "Открыть готовый DOCX"),
                new("profiles", "Справочник СТО и комплектов"),
                new("setup", "Установить / восстановить зависимости")], "Выход");
            if (action is null) return;
            try
            {
                if (action == "new") Create(root);
                else if (action == "profiles")
                {
                    foreach (var profile in WorkspaceCommands.Profiles(root).Profiles.Values)
                        Console.WriteLine("\n" + profile.Label + "\n  " + profile.Description);
                }
                else if (action == "setup") EnvironmentSetup.Run(root, development: false);
                else
                {
                    var names = WorkspaceCommands.Projects(root);
                    if (names.Length == 0)
                    {
                        Console.WriteLine("Работ пока нет. Выберите «Создать работу».");
                        continue;
                    }
                    var name = Select("Выберите работу", names.Select(n => new Choice(n, n)).ToArray());
                    if (name is null) continue;
                    if (action == "build") WorkspaceCommands.Build(root, name);
                    else
                    {
                        var path = WorkspaceCommands.Project(root, name);
                        if (action == "document")
                        {
                            path = Path.Combine(path, "_output", "chapters", "main.docx");
                            if (!File.Exists(path))
                            {
                                Console.WriteLine("Готового DOCX ещё нет. Сначала выберите «Собрать DOCX».");
                                continue;
                            }
                        }
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                        Console.WriteLine("Открыто: " + path);
                    }
                }
            }
            catch (Exception error) { Console.Error.WriteLine("Не удалось выполнить действие: " + error.Message); }
            Console.WriteLine();
        }
    }

    private static void Create(string root)
    {
        var catalog = WorkspaceCommands.Profiles(root);
        var id = Select("Выберите тип работы и СТО", catalog.Profiles
            .OrderBy(p => p.Key != catalog.Default)
            .Select(p => new Choice(p.Key, p.Value.Label, p.Value.Description)).ToArray());
        if (id is null) return;
        Console.WriteLine("Комплект: " + catalog.Profiles[id].Label);
        while (true)
        {
            string? name;
            if (Interactive)
                name = AnsiConsole.Prompt(new TextPrompt<string>("Название новой работы (Enter — назад):").AllowEmpty()).Trim();
            else
            {
                Console.Write("Название новой работы (Enter — назад): ");
                name = Console.ReadLine()?.Trim();
            }
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                var project = WorkspaceCommands.Project(root, name);
                if (Directory.Exists(project) || File.Exists(project))
                {
                    Console.WriteLine("Такая папка уже существует. Выберите другое название.");
                    continue;
                }
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Недопустимое название. Укажите имя одной папки без символов < > : \" / \\ | ? *, точки в конце и служебных имён Windows (CON, NUL и т. п.).");
                continue;
            }
            WorkspaceCommands.Create(root, name, id);
            return;
        }
    }

    private static string? Select(string title, Choice[] choices, string back = "Назад")
    {
        var cancel = new Choice("", back);
        if (Interactive)
        {
            Console.WriteLine("↑/↓ — выбор, Enter — подтвердить, Esc — " + back.ToLowerInvariant());
            var selected = AnsiConsole.Prompt(new SelectionPrompt<Choice>
            {
                Title = Markup.Escape(title), PageSize = 8, WrapAround = true, CancelResult = () => cancel,
                MoreChoicesText = "[grey]↑/↓ — остальные работы[/]",
                Converter = item => Markup.Escape(item.Label) + (item.Description.Length == 0 ? "" : "\n  " + Markup.Escape(item.Description))
            }.AddChoices(choices.Append(cancel)));
            return selected == cancel ? null : selected.Id;
        }

        // Pipes and terminals without interactive support retain a simple numbered menu.
        Console.WriteLine("\n" + title);
        for (var i = 0; i < choices.Length; i++)
            Console.WriteLine($"  {i + 1}. {choices[i].Label}" + (choices[i].Description.Length == 0 ? "" : "\n     " + choices[i].Description));
        Console.WriteLine("  0. " + back);
        while (true)
        {
            Console.Write("Номер: ");
            var input = Console.ReadLine()?.Trim();
            if (input is null or "0" or "") return null;
            if (int.TryParse(input, out var number) && number >= 1 && number <= choices.Length)
                return choices[number - 1].Id;
            Console.WriteLine("Выберите номер из списка или 0.");
        }
    }
}
