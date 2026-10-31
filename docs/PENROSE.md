# Диаграммы Penrose

Penrose используется для схем и отношений между объектами; численные научные графики сохраняют обычный путь ggplot2. Официальный CLI `@penrose/roger` устанавливается командой `ANK-DOCX.cmd setup` вместе с локальным Node.js. Бинарные файлы, npm-пакеты и кэш находятся в `.tools/`, в Git хранятся версии и npm lock-файл. Системный Node.js и глобальная установка npm-пакета не требуются.

## Исходники отдельно от текста

Принятый способ — отдельная папка диаграммы и обычная ссылка на рисунок в QMD:

```text
figures/penrose/sets/
  diagram.domain
  diagram.substance
  diagram.style
  diagram.trio.json
```

- `.domain` задаёт типы объектов и отношений.
- `.substance` описывает конкретные объекты и связи.
- `.style` задаёт геометрию, подписи и ограничения размещения.
- `.trio.json` связывает файлы и сохраняет `variation` для повторяемой компоновки.

Это стандартные форматы Penrose. Несколько диаграмм могут ссылаться на общие Domain/Style, копировать их в каждую папку не обязательно. Собственного синтаксиса блоков Penrose в QMD нет.

Пример штатного `.trio.json`:

```json
{
  "domain": "diagram.domain",
  "substance": "diagram.substance",
  "style": ["diagram.style"],
  "variation": "ANK-example-sets-2026"
}
```

В `chapters/main.qmd` остаются подпись и ссылка:

```qmd
На рисунке [-@fig-sets] показаны отношения между множествами.

![Отношения между множествами](../.quarto/penrose/sets/diagram.svg){#fig-sets width=80%}
```

При сборке штатный `project.pre-render` Quarto вызывает `tools/academic.R diagrams`. Общий скрипт расширения обрабатывает `.trio.json` под `figures/penrose/` и сохраняет SVG с тем же относительным деревом в `.quarto/penrose/`. Если диаграмм нет, шаг ничего не рендерит. При ошибке CLI сборка прекращается: прежний SVG не используется как успешный результат.

Затем обычный механизм Quarto/Pandoc вставляет SVG, а общий адаптер ANK формирует подпись, SEQ/REF и статистику. Для совместимости с Word `rsvg-convert` переводит текст SVG в векторные контуры: Word не сохранял размер букв из CSS-сокращения `font`, создаваемого Penrose. Результат остаётся векторным; геометрия и надписи редактируются в исходниках Penrose.

## Использование

```cmd
ANK-DOCX.cmd setup
ANK-DOCX.cmd build "Пример"
```

Вне полной сборки можно вызвать официальный CLI через локальное окружение:

```cmd
ANK-DOCX.cmd penrose trio --trio "путь/diagram.trio.json" --out "путь/diagram.svg"
```

Такой вызов создаёт исходный SVG Penrose; совместимость Word обрабатывает pre-render при сборке. Авторские файлы агента находятся в папке диаграммы, SVG в `.quarto/` генерируются заново и в Git не добавляются. После изменения связей или Style агент должен посмотреть полученную картинку: ограничения размещения не проверяют правильность научного содержания.

Новые работы получают pre-render из Starter. Для старой работы нужны обновлённое расширение, точка входа `tools/academic.R` из Starter и настройка:

```yaml
project:
  pre-render:
    - tools/academic.R diagrams
```

Эта строка дополняет существующий блок `project`, не создаёт второй. Минимальные версии: расширение 0.4.3 и сервис 0.4.4. Пользовательские проекты автоматически не переписываются.

Проверенные версии — Node.js 24.22.2, roger 3.3.1. Особенности зависимостей и приёмка — [PENROSE_INTEGRATION.md](research/PENROSE_INTEGRATION.md). Официальные источники: [CLI и форматы](https://penrose.cs.cmu.edu/docs/ref/using), [исходники и MIT-лицензия](https://github.com/penrose/penrose), [pre-render Quarto](https://quarto.org/docs/projects/scripts.html).
