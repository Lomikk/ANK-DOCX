-- Validate the author-owned Pandoc tree before the Word adapter runs.
-- BibTeX parsing and bibliography generation remain Pandoc/Citeproc duties.
local text = pandoc.utils.stringify
local directory = pandoc.path.directory(PANDOC_SCRIPT_FILE)
local function fail(message) assert(false, 'ANK: '..message) end
local function read_file(path)
  local file = io.open(path, 'rb')
  if not file and quarto.doc.input_file then
    file = io.open(pandoc.path.join({pandoc.path.directory(quarto.doc.input_file), path}), 'rb')
  end
  if not file then return nil end
  local contents = file:read('*a'); file:close(); return contents
end
local function managed_file(value, names, setting)
  local path = text(value)
  local contents = read_file(path)
  for _, name in ipairs(names) do
    if contents and pandoc.path.filename(path) == name and contents == read_file(pandoc.path.join({directory, name})) then return end
  end
  fail('Параметр '..setting..' управляется academic-docx. Удалите переопределение из настроек работы.')
end
local reserved = {}
for _, name in ipairs({'оглавление','содержание','титульный лист','аннотация','реферат',
  'задание','библиографический список','список литературы','литература','references','bibliography'}) do reserved[name] = true end
local function title(h)
  return pandoc.text.lower(text(h)):gsub('%s+', ' '):gsub('^%s+', ''):gsub('%s+$', '')
end
local function object_id(id) return id:match('^(%a+)%-') end

-- Pandoc already parses line blocks and math. Validate their structure, not QMD/TeX.
local function significant(inlines)
  local result = {}
  for _, inline in ipairs(inlines) do
    if inline.t ~= 'Space' and inline.t ~= 'SoftBreak' then result[#result+1] = inline end
  end
  return result
end
local function display_formula(block)
  if block.t ~= 'Para' and block.t ~= 'Plain' then return false end
  for _, inline in ipairs(block.content) do
    if inline.t == 'Math' and inline.mathtype == 'DisplayMath' then return true end
  end
  return false
end
local function definition_line(inlines)
  local parts, has_math = significant(inlines), false
  for i, part in ipairs(parts) do
    if part.t == 'Math' then
      has_math = true
      local following = parts[i+1]
      if following and following.t == 'Str' and
        (following.text == '—' or following.text == '–' or following.text == '-') then return true end
    end
  end
  local first = parts[1] and pandoc.text.lower(text(parts[1])):gsub('[:,]$', '') or ''
  return has_math and first == 'где'
end
local function unstyled_definitions(block)
  if block.t == 'Para' or block.t == 'Plain' then return definition_line(block.content) end
  if block.t == 'LineBlock' then
    for _, line in ipairs(block.content) do if definition_line(line) then return true end end
  end
  return false
end
local function validate_definitions(div, id)
  local function invalid(line, message)
    fail('Пояснения формулы '..(id or '(без метки)')..(line and ', строка '..line or '')..': '..message)
  end
  if #div.content ~= 1 or div.content[1].t ~= 'LineBlock' or #div.content[1].content == 0 then
    invalid(nil, 'в Formula Definitions нужен один Markdown line block: каждое обозначение с новой строки «|».')
  end
  local lines = div.content[1].content
  for n, line in ipairs(lines) do
    local parts = significant(line)
    local start = 1
    if n == 1 then
      if not parts[1] or parts[1].t ~= 'Str' or parts[1].text ~= 'где' then
        invalid(n, 'начните первую строку с «| где $v$ — ...;», без двоеточия после «где».')
      end
      start = 2
    end
    if not parts[start] or parts[start].t ~= 'Math' or parts[start].mathtype ~= 'InlineMath' or
      not parts[start+1] or parts[start+1].t ~= 'Str' or parts[start+1].text ~= '—' then
      invalid(n, 'нужно одно обозначение и тире: «| $s$ — пояснение;». Не объединяйте «$a$ и $b$ — ...».')
    end
    local description = pandoc.Inlines({})
    for i = start+2, #parts do
      local part = parts[i]
      if part.t == 'Math' and parts[i+1] and parts[i+1].t == 'Str' and parts[i+1].text == '—' then
        invalid(n, 'перенесите следующее обозначение на отдельную строку «|».')
      end
      description:insert(part)
    end
    local value = text(description)
    local ending = n == #lines and '.' or ';'
    if value:sub(-1) ~= ending then invalid(n, 'строка должна заканчиваться '..(ending == '.' and 'точкой.' or 'точкой с запятой.')) end
    if not value:sub(1, -2):match('%S') then invalid(n, 'добавьте расшифровку обозначения.') end
    if value:sub(1, -2):find(';', 1, true) then
      invalid(n, 'точка с запятой разделяет строки пояснений; каждое обозначение пишите с новой строки «|».')
    end
  end
end

local floats = {}
return {{FloatRefTarget = function(float) floats[float.identifier] = true end}, {Pandoc = function(doc)
  -- Some writer options are removed from document metadata before Lua runs.
  -- Inspect the effective format through Quarto's public CLI instead of parsing YAML.
  local ok, inspected = pcall(pandoc.pipe, 'quarto', {'inspect', quarto.doc.input_file}, '')
  if not ok then fail('Не удалось проверить настройки Quarto: '..tostring(inspected)) end
  local configuration = quarto.json.decode(inspected)
  local format
  for _, candidate in pairs(configuration.formats or {}) do
    if candidate.identifier and tostring(candidate.identifier['extension-name'] or ''):match('[^/]+$') == 'academic' then format = candidate end
  end
  if not format then fail('Не найден формат academic-docx в quarto inspect.') end
  if format.pandoc['number-sections'] then fail('Нумерацией разделов управляет шаблон. Удалите number-sections: true.') end
  if format.pandoc.citeproc == false then fail('Нельзя отключать Citeproc. Источники хранятся в references.bib.') end
  if doc.meta['toc-title'] and text(doc.meta['toc-title']) ~= 'Оглавление' then
    fail('Заголовок оглавления задаёт шаблон. Удалите toc-title; оглавление создаётся автоматически.')
  end
  for _, key in ipairs({'academic-front-matter','references','reference-section-title'}) do
    if doc.meta[key] then fail('Параметр '..key..' создаётся сборщиком. Источники храните в references.bib.') end
  end
  if doc.meta['suppress-bibliography'] == true or doc.meta.citeproc == false then
    fail('Нельзя отключать автоматическую библиографию/Citeproc. Используйте references.bib и [@ключ].')
  end
  if doc.meta.csl then managed_file(doc.meta.csl, {'gost-r-7-0-5-2008-numeric.csl'}, 'csl') end
  if doc.meta['reference-doc'] then managed_file(doc.meta['reference-doc'], {'academic.docx','academic-monochrome.docx'}, 'reference-doc') end
  if doc.meta.template then managed_file(doc.meta.template, {'document.openxml'}, 'template') end
  managed_file(format.pandoc['reference-doc'], {'academic.docx','academic-monochrome.docx'}, 'reference-doc')
  managed_file(format.pandoc.template, {'document.openxml'}, 'template')
  if doc.meta.bibliography and pandoc.path.filename(text(doc.meta.bibliography)) ~= 'references.bib' then
    fail('Источники работы хранятся в references.bib. Удалите переопределение bibliography.')
  end
  if PANDOC_WRITER_OPTIONS.reference_doc then
    managed_file(PANDOC_WRITER_OPTIONS.reference_doc, {'academic.docx','academic-monochrome.docx'}, 'reference-doc')
  end
  if PANDOC_WRITER_OPTIONS.number_sections or doc.meta['number-sections'] == true then fail('Нумерацией разделов управляет шаблон. Удалите number-sections: true.') end
  -- Compare templates through Pandoc's own compiled-template API, not an XML parser.
  local file = assert(io.open(pandoc.path.join({directory, 'document.openxml'}), 'r'))
  local expected = pandoc.template.compile(file:read('*a')); file:close()
  local context = {body='BODY', toc='TOC', sectpr='SECTION', ['academic-front-matter']='FRONT'}
  if pandoc.layout.render(pandoc.template.apply(PANDOC_WRITER_OPTIONS.template, context)) ~=
     pandoc.layout.render(pandoc.template.apply(expected, context)) then
    fail('Шаблон DOCX управляется academic-docx. Удалите template из настроек работы.')
  end

  local ids, citations, top, issues = floats, {}, {}, {}
  local function identify(e) if e.identifier and e.identifier ~= '' then ids[e.identifier] = true end end
  doc:walk({
    Blocks = function(blocks)
      for i, block in ipairs(blocks) do
        local previous = blocks[i-1]
        local after_formula = previous and display_formula(previous)
        local id = after_formula and text(previous):match('{#(eq%-[%w_-]+)}$') or nil
        if block.t == 'Div' and block.attributes['custom-style'] == 'Formula Definitions' then
          validate_definitions(block, id)
        elseif after_formula and unstyled_definitions(block) then
          fail('Пояснения формулы '..(id or '(без метки)')..': вместо обычного абзаца используйте Div custom-style="Formula Definitions" и строки «| где $v$ — ...;», «| $s$ — ... .». Каждое обозначение — с новой строки.')
        end
      end
    end,
    Header = function(h)
      identify(h)
      if reserved[title(h)] then issues[#issues+1] = 'Раздел «'..text(h)..'» создаёт сборщик. Удалите его из QMD; источники — в references.bib, ссылки — [@ключ].' end
      if h.level == 1 then
        top[#top+1] = h
      end
    end,
    Div = function(d)
      if d.identifier == 'refs' or d.classes:includes('references') then
        issues[#issues+1] = 'Блок refs создаётся автоматически перед приложениями. Удалите его из QMD.'
      end
      identify(d)
    end,
    Span=identify, Figure=identify, Image=identify, Table=identify, CodeBlock=identify,
    Para = function(p)
      -- Quarto's standard display-math label is still text at this filter stage.
      local id = text(p):match('{#(eq%-[%w_-]+)}$')
      if id then ids[id] = true end
    end,
    Cite = function(c)
      for _, citation in ipairs(c.citations) do citations[#citations+1] = citation end
    end
  })
  if #issues > 0 then fail(issues[1]) end
  local sources = {}
  for _, reference in ipairs(pandoc.utils.references(doc)) do sources[text(reference.id)] = true end
  local has_sources = false
  for _, citation in ipairs(citations) do
    local id, kind = citation.id, object_id(citation.id)
    if kind == 'eq' or kind == 'fig' or kind == 'tbl' or kind == 'sec' or kind == 'lst' then
      if not ids[id] then fail('Объект @'..id..' не найден. Добавьте его стандартную метку или исправьте ссылку.') end
      if kind == 'eq' and citation.mode == 'SuppressAuthor' then
        fail('Для формулы используйте @'..id..' без [-...] и внешних скобок: номер со скобками создаёт сборщик.')
      end
    else
      if not sources[id] then fail('Источник @'..id..' не найден в references.bib. Добавьте достоверную запись или исправьте ключ.') end
      has_sources = true
    end
  end
  local function prose(p)
    if reserved[pandoc.text.lower(text(p)):gsub('^%s+',''):gsub('%s+$','')] then
      issues[#issues+1] = 'Сопроводительный заголовок «'..text(p)..'» создаёт сборщик. Удалите его из авторского текста.'
    end
    local plain = p:walk({Math=function() return {} end, Code=function() return {} end,
      RawInline=function() return {} end, Cite=function() return {} end, Link=function() return {} end})
    for value in text(plain):gmatch('%[(%d[^%[%]]*)%]') do
      local remainder = value:gsub('с%.', ''):gsub('p%.', ''):gsub('–',''):gsub('—',''):gsub('[%d%s,;%.%-]','')
      if remainder == '' then issues[#issues+1] = 'Обнаружена ручная числовая ссылка ['..value..']. Для источника используйте [@ключ] из references.bib; математические индексы оформляйте как математику или код.' end
    end
  end
  doc:walk({Para=prose, Plain=prose, Inlines=function(inlines)
    for i, inline in ipairs(inlines) do
      if inline.t == 'Cite' and #inline.citations == 1 and inline.citations[1].id:match('^eq%-') then
        local before, after = inlines[i-1], inlines[i+1]
        if before and after and before.t == 'Str' and after.t == 'Str' and before.text:match('%($') and after.text:match('^%)') then
          issues[#issues+1] = 'Не добавляйте внешние скобки к @'..inline.citations[1].id..': скобки формулы создаёт сборщик.'
        end
      end
    end
  end})
  if #issues > 0 then fail(issues[1]) end
  if not has_sources and not doc.meta.nocite then return doc end
  local first_appendix
  for _, h in ipairs(top) do if h.classes:includes('appendix') then first_appendix = h.identifier; break end end
  if not first_appendix and doc.meta.book and doc.meta.book.appendices and #doc.meta.book.appendices > 0 then
    local numbered = {}
    for _, h in ipairs(top) do if not h.classes:includes('unnumbered') then numbered[#numbered+1] = h end end
    local h = numbered[#numbered - #doc.meta.book.appendices + 1]
    if h then first_appendix = h.identifier end
  end
  local blocks = pandoc.List()
  local inserted = false
  local function bibliography()
    blocks:insert(pandoc.Header(1, 'Библиографический список', pandoc.Attr('academic-bibliography', {'unnumbered'})))
    blocks:insert(pandoc.Div({}, pandoc.Attr('refs')))
    inserted = true
  end
  for _, block in ipairs(doc.blocks) do
    local contains = block.t == 'Header' and block.identifier == first_appendix
    if first_appendix then block:walk({Header=function(h) if h.identifier == first_appendix then contains = true end end}) end
    if contains and not inserted then bibliography() end
    blocks:insert(block)
  end
  if not inserted then bibliography() end
  doc.blocks = blocks
  return doc
end}}
