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
    if paragraph_text(p) then
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
    local kind = id:match('^tbl%-') and 'таблицей' or id:match('^fig%-') and 'рисунком' or nil
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
