# Отдельные примеры

## Основной текст

`main-text.qmd` и актуальный `main-text-math-rules.docx` показывают общий reference-docx: основной текст, четыре уровня заголовков, ненумерованные структурные заголовки и inline-обозначения OMML. Титула и сопроводительных листов нет. Предыдущий `main-text.docx` сохранён отдельно: он был открыт в Word при обновлении. Параметры и основания: [REFERENCE_DOCX.md](../../docs/REFERENCE_DOCX.md). Нумеруемые формулы и пояснения новых обозначений показаны в `../model.qmd`; правила — в [MATH_RULES.md](../../docs/MATH_RULES.md).

Из корня репозитория, при доступном Quarto, образец воспроизводится без R:

```powershell
quarto pandoc academic/examples/main-text.qmd --from markdown --to docx --reference-doc=academic/_extensions/academic/academic.docx --output=academic/examples/main-text-math-rules.docx
```

Эта команда использует стандартный Pandoc и explicit custom-style из образца; полный адаптер Book не вызывается.

## Графика

После восстановления основного renv запустите из academic/:

```powershell
Rscript examples/damped-sine.R
```

Получатся SVG и PNG 600 dpi в output/graphics/ и векторный PDF в output/pdf/ корня репозитория. Рядом с иллюстрациями сохраняются рассчитанные данные CSV. Дополнительный аргумент задаёт корень вывода.

В начале R-файла задаются A, tau и nu. Само выражение model используется для вычисления значений и математического заголовка plotmath. Числа в подзаголовке берутся из того же набора параметров. График создаётся ggplot2; TeX, PGFPlots и quarto_tikz не вызываются.

Огибающие заданы для этой конкретной затухающей синусоиды. При замене модели на другую функцию следует пересмотреть и их смысл. Пример не является универсальным символьным решателем.

## Листинги

`listings.qmd` показывает **неисполняемые** нумеруемые листинги Python и SQL со штатными метками `#lst-…` и перекрёстными ссылками `@lst-…`. Фрагменты являются редактируемым текстом Word, не скриншотами и не заданиями для knitr. Стиль основного проекта — `syntax-highlighting: github`. В DOCX цвета токенов сохраняются в Word-стилях reference-docx, поэтому для монохромного результата необходимо выбрать **одновременно** `syntax-highlighting: monochrome` и `_extensions/academic/academic-monochrome.docx` в качестве `reference-doc`. Эти два режима — альтернативные сборки одного исходника, а не два разных типа разметки.

Для визуальной проверки можно выполнить обычный Pandoc smoke-test кода без рендеринга Book:

```powershell
pandoc academic/examples/listings.qmd --from markdown --to docx --reference-doc=academic/_extensions/academic/academic.docx --highlight-style=pygments --output=academic/examples/listings-pandoc.docx
```

Монохромная проверка Pandoc (при необходимости):

```powershell
pandoc academic/examples/listings.qmd --from markdown --to docx --reference-doc=academic/_extensions/academic/academic-monochrome.docx --highlight-style=monochrome --output=academic/examples/listings-monochrome-pandoc.docx
```

Внешний Pandoc 3.1.3 предлагает тему `pygments`, а Quarto 1.10 — ещё и `github`. Pandoc без Quarto **не выполняет** `lst-` нумерацию и перекрёстные ссылки. Подписи/нумерацию, их размещение и специальное оформление приложений нужно проверять полноценной сборкой Quarto на Windows по [LISTING_RULES.md](../../docs/LISTING_RULES.md).
