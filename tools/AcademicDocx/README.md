# Служебное приложение Academic DOCX

Один C#-инструмент для подтверждённых пробелов Quarto/Officeverse: финализация Word, продолжения таблиц, проверки объектов, экспорт PDF, настройка reference-docx и подготовка окружения. Quarto продолжает управлять сборкой работы. Прямые преобразования исходного пакета в `finalize.R` пока сохранены; их перенос в SDK — отдельный следующий участок.

## Установка и сборка

Для пользователя `ANK-DOCX.cmd setup` получает готовое приложение с собственным .NET Runtime. .NET SDK и PIA нужны разработчику для компиляции. Ему требуются Windows, .NET 10 SDK и официальные Word PIA и Office core PIA из установленного Office или Visual Studio Tools for Office. PIA определяются по стандартным каталогам средств разработки и GAC. При отсутствии либо неоднозначности укажите пути явно:

```powershell
.\tools\build-service.cmd '-p:WordPiaPath=C:/path/to/Microsoft.Office.Interop.Word.dll' '-p:OfficePiaPath=C:/path/to/office.dll'
```

Не устанавливайте неподдерживаемую NuGet-перепаковку `Microsoft.Office.Interop.Word`. Использованные interop-типы встраиваются компилятором; распространять отдельную DLL PIA с приложением не требуется для проверенных сценариев.

Для разработки без восстановления R-окружения:

```powershell
dotnet build tools/AcademicDocx/AcademicDocx.csproj -p:RestoreLockedMode=true
dotnet publish tools/AcademicDocx/AcademicDocx.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o .tools/academic-docx
```

Публикация включает .NET runtime. Сохраняйте весь каталог публикации, а не только EXE. `tools\pack-service.cmd` готовит архив и закреплённый SHA-256 для начального загрузчика. Настольный Word нужен командам, использующим COM. Переносимая библиотека `AcademicDocx.Core` не ссылается на Word/PIA и проверяется отдельно. Устройство локального окружения и порядок выпуска — [ENVIRONMENT_SETUP.md](../../docs/ENVIRONMENT_SETUP.md).

Для независимых работ добавьте каталог публикации в PATH либо задайте `ACADEMIC_DOCX_SERVICE`. Команда `--version` сообщает версию приложения; расширение 0.4.2 требует приложение не ниже 0.4.2 в ветке 0.4.x: единый каталог комплектов, явный выбор оформления страниц и приложения НИР. Настройка окружения терминала также должна обеспечивать Quarto, R и rsvg-convert. `setup` не меняет глобальный PATH. Новый авторский проект создаётся из [Starter](../../quarto/), отдельный CLI создания не нужен.

## Команды из корня

```powershell
.\tools\academic-docx.cmd help
.\tools\academic-docx.cmd setup .
.\tools\academic-docx.cmd validate examples/book/_book/coursework.docx
.\tools\academic-docx.cmd style-reference quarto/_extensions/academic/academic.docx --output build/reference.docx
.\tools\academic-docx.cmd style-reference quarto/_extensions/academic/academic.docx --output build/reference-sto04.docx --bottom-margin-mm 20
.\tools\academic-docx.cmd finalize-word build/input.docx --report build/finalize.json
.\tools\academic-docx.cmd inspect-word examples/book/_book/coursework.docx build/inspect.json
.\tools\academic-docx.cmd check-word examples/book/_book/coursework.docx build/mutated.docx build/check.json --expected-pages 11
.\tools\academic-docx.cmd export-pdf examples/book/_book/coursework.docx output/coursework.pdf build/export.json
```

`finalize-word` вызывается автоматически из Quarto hook. Вручную он нужен для диагностики уже подготовленного промежуточного DOCX, а не для преобразования произвольного файла Quarto в наш формат. Работает с временной копией и заменяет целевой файл после финализации, проверки схемы SDK и корректного завершения Word. Исходный файл при сбое не заменяется. Счётчик ошибок схемы до Word не обрезается на стандартном лимите SDK.

Для технической курсовой hook передаёт `--layout-metadata` (внутренний JSON, созданный из авторского YAML) и `--page-templates` (компоненты установленного расширения). Команда разработчика `prepare-sto04 SOURCE.docx DIRECTORY` воспроизводит подготовку компонентов из проверенного преподавательского исходника; при обычной сборке она не нужна. Основания, размеры и границы поддержки — [в отчёте](../../docs/research/STO04_PAGE_LAYOUT.md).

`validate` возвращает ненулевой код при ошибках схемы. `check-word` меняет только отдельную копию; источник и копия должны иметь разные пути. Ожидаемые объекты читаются из входного пакета. При наличии подходящих объектов проверяются изменение степени в OMML, нумерация таблиц и REF после вставки дополнительной подписи, обновление заголовка в TOC, сохранение и повторное открытие. Неприменимые изменения явно перечисляются в `skipped`. Флаг `--expected-pages` проверяет исходный документ по известному печатному числу страниц; он не меняет поле NUMPAGES.

`export-pdf` раздельно фиксирует `exported`, `closed` и жизненный цикл собственного Word. Если PDF создан, а закрытие не удалось, команда возвращает ошибку и сохраняет диагностический отчёт. Существовавшие процессы Word не завершаются.

`setup` использует версии и хэши из корневого `dependencies.lock.json`. ZIP проверяются SHA-256, установщик R — штатным Windows WinVerifyTrust для Authenticode. Это замена Get-AuthenticodeSignature; извлечение сертификата само по себе не считалось бы проверкой подписи. PATH и локаль пользователя не изменяются: LC_* очищаются только для дочернего процесса восстановления renv. Новая установка R и загрузки выполняются, только если инструмент отсутствует; существующие версии проверяются.

## Проверки разработчика

Переносимые проверки, не запускающие Word:

```powershell
dotnet test tools/AcademicDocx.Tests/AcademicDocx.Tests.csproj -p:RestoreLockedMode=true --filter FullyQualifiedName~ReferenceTests
```

Интеграционные проверки на отдельной Windows-машине с Word и опубликованным приложением:

```powershell
$env:ACADEMIC_RUN_WORD_TESTS = '1'
$env:ACADEMIC_DOCX_SERVICE = (Resolve-Path .tools/academic-docx/academic-docx.exe).Path
dotnet test tools/AcademicDocx.Tests/AcademicDocx.Tests.csproj -p:RestoreLockedMode=true
```

Без явного флага Word-тесты пропускаются. Они проверяют фиксированный корпус миграции: полную таблицу с объединениями/OMML, один/два/три фрагмента, сдвиг предшествующим текстом и отказ для неподдерживаемых таблиц с сохранением входного файла. Продуктовая проверка произвольного документа не использует фиксированные количества из этого корпуса.

`QuartoStarterTests` дополнительно требует `quarto`, `Rscript` и rsvg-convert в PATH. Проверяет создание двух независимых работ штатным Starter, namespace расширения, профили и ошибки метаданных, OMML/REF и схему после Word. Дочерние сборки находят сервис через PATH. Тест выполняется из checkout; опубликованный GitHub-выпуск этим не проверяется.

Для сохранения временных файлов тестов задайте `ACADEMIC_TEST_ARTIFACTS` каталогом под `build/`. По умолчанию тесты удаляют только собственные временные каталоги. На странице [архитектуры](../../docs/SERVICE_LAYER_ARCHITECTURE.md) приведены границы подтверждённой поддержки.
