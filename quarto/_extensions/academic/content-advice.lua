local chapter, has_text, previous = nil, false, nil
local function recommend(message)
  -- Separate diagnostics from subsequent Quarto/Word output in the short log.
  io.stderr:write('Рекомендация: '..message..'\n\n')
end
local function meaningful(block)
  return pandoc.utils.stringify(block):match('%S') ~= nil
end
local function paragraph_text(p)
  -- Alternative text, raw markup and a standalone formula are not prose.
  local prose = p:walk({Image = function() return {} end,
    RawInline = function() return {} end, Math = function() return {} end})
  return meaningful(prose)
end
local function has_reference(para, id)
  local found = false
  if para then
    para:walk({Cite = function(c)
      for _, citation in ipairs(c.citations) do
        if citation.id == id then found = true end
      end
    end})
  end
  return found
end
local numbered_words = {}
for _, word in ipairs({
  'рисунок','рисунка','рисунку','рисунком','рисунке','Рисунок','Рисунка','Рисунку','Рисунком','Рисунке',
  'таблица','таблицы','таблице','таблицу','таблицей','Таблица','Таблицы','Таблице','Таблицу','Таблицей',
  'листинг','листинга','листингу','листингом','листинге','Листинг','Листинга','Листингу','Листингом','Листинге',
  'формула','формулы','формуле','формулу','формулой','Формула','Формулы','Формуле','Формулу','Формулой'
}) do numbered_words[word] = true end
local function manual_references(p)
  -- Work with prose only; citations, links, literal code and math are not prose.
  local plain = p:walk({Code=function() return {} end, Math=function() return {} end,
    Cite=function() return {} end, Link=function() return {} end,
    Image=function() return {} end, RawInline=function() return {} end})
  local words = {}
  for word in pandoc.utils.stringify(plain):gmatch('%S+') do words[#words+1] = word end
  for i=1,#words-1 do
    local word = words[i]:gsub('^[%(%["«]+',''):gsub('[%]%):,;]+$','')
    local number = words[i+1]:gsub('^[%(]+',''):gsub('[%)]*[.,;:]$',''):gsub('[%)]+$','')
    if numbered_words[word] and number:match('^%d[%d.]*$') and not number:match('%.%.') then
      local shown = words[i+1]:gsub('[,;:.]+$','')
      recommend('найдено «'..word..' '..shown..'»; если это объект данной работы, рассмотрите автоматическую ссылку на его ID.')
    end
  end
end
local function display_math(p)
  for _, inline in ipairs(p.content) do
    if inline.t == 'Math' and inline.mathtype == 'DisplayMath' then return true end
  end
  return false
end
return {
  traverse = 'topdown',
  Header = function(h)
    previous = nil
    if h.level == 1 then
      chapter, has_text = pandoc.utils.stringify(h), false
    elseif h.level == 2 and chapter then
      if not has_text then
        recommend('раздел «'..chapter..'» начинается сразу с подраздела; рассмотрите возможность добавить вводный текст.')
      end
      chapter = nil
    end
  end,
  Para = function(p)
    if display_math(p) then
      local id = pandoc.utils.stringify(p):match('{#(eq%-[%w-]+)}$')
      if id and not has_reference(previous, id) then
        recommend('перед формулой '..id..' не найдено упоминание; рассмотрите возможность добавить вводный абзац со ссылкой.')
      end
      previous = nil
      return nil, false
    end
    if paragraph_text(p) then
      manual_references(p)
      if chapter then has_text = true end
      previous = p
    else
      local has_image = false
      p:walk({Image = function() has_image = true end})
      if has_image then previous = nil end
    end
  end,
  FloatRefTarget = function(float)
    local id = float.identifier or ''
    local kind = id:match('^tbl%-') and 'таблицей' or id:match('^fig%-') and 'рисунком' or id:match('^lst%-') and 'листингом' or nil
    if kind and not float.parent_id then
      if not has_reference(previous, id) then
        recommend('перед '..kind..' '..id..' не найдено упоминание; рассмотрите возможность добавить вводный абзац со ссылкой.')
      end
      previous = nil
    end
    return nil, false
  end,
  BulletList = function(b) if chapter and meaningful(b) then has_text = true end; previous = nil; return nil, false end,
  OrderedList = function(b) if chapter and meaningful(b) then has_text = true end; previous = nil; return nil, false end,
  BlockQuote = function(b) if chapter and meaningful(b) then has_text = true end; previous = nil; return nil, false end,
  Table = function() previous = nil; return nil, false end,
  Figure = function() previous = nil; return nil, false end,
}
