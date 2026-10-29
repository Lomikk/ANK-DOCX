-- Use ordinary Quarto IDs; only Word fields and front matter are added here.
local text = pandoc.utils.stringify
local function esc(s) return tostring(s):gsub('&','&amp;'):gsub('<','&lt;'):gsub('>','&gt;'):gsub('"','&quot;') end
local function bookmark(id) return id:gsub('[^%w_]','_') end
local function field(code)
  return '<w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r><w:r><w:instrText xml:space="preserve"> '..esc(code)..' </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>0</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r>'
end
local bid=15000
local function number(id,sequence,prefix,suffix)
  bid=bid+1
  return pandoc.RawInline('openxml','<w:r><w:t xml:space="preserve">'..esc(prefix)..'</w:t></w:r><w:bookmarkStart w:id="'..bid..'" w:name="'..bookmark(id)..'"/>'..field('SEQ '..sequence..' \\* ARABIC')..'<w:bookmarkEnd w:id="'..bid..'"/><w:r><w:t xml:space="preserve">'..esc(suffix)..'</w:t></w:r>')
end
local function pagebreak() return pandoc.RawBlock('openxml','<w:p><w:r><w:br w:type="page"/></w:r></w:p>') end
local function styled(s,style)
  return pandoc.Div({pandoc.Para(s)},{['custom-style']=style})
end
-- Let Pandoc parse standard YAML; this is a small fixed profile configuration,
-- not an author-side include manifest or a new template/plugin language.
local profile_file=assert(io.open(pandoc.path.join({
  pandoc.path.directory(PANDOC_SCRIPT_FILE),'profiles.yml'}),'rb'))
local profile_config=pandoc.read('---\n'..profile_file:read('*a')..'\n---\n','markdown').meta
profile_file:close()
local function metadata_value(meta,path)
  local value=meta
  for key in path:gmatch('[^.]+') do
    if type(value)~='table' then return '' end
    value=value[key]
  end
  return value and text(value) or ''
end
local function front_matter(doc)
  local id=metadata_value(doc.meta,'academic.profile')
  local profile=profile_config.profiles[id]
  if not profile then
    assert(false,'Unknown or missing academic.profile: "'..id..'". Available profiles: coursework, referat')
  end
  local missing={}
  for _,sheet_id in ipairs(profile['before-body']) do
    local sheet=profile_config.sheets[text(sheet_id)]
    assert(sheet,'Unknown sheet in academic profile '..id..': '..text(sheet_id))
    for _,required in ipairs(sheet.required or {}) do
      local path=text(required)
      if metadata_value(doc.meta,path):match('^%s*$') then
        missing[path]=true
      end
    end
  end
  local fields={}
  for path in pairs(missing) do fields[#fields+1]=path end
  table.sort(fields)
  if #fields>0 then
    -- Quarto's filter environment supplies a non-throwing error logger.
    -- The standard Lua assertion must abort instead of producing a blank sheet.
    assert(false,'Missing metadata for academic.profile "'..id..'": '..table.concat(fields,', '))
  end
  local front=pandoc.Blocks({})
  local function value(path) return metadata_value(doc.meta,path) end
  for _,sheet_id in ipairs(profile['before-body']) do
    local sheet_name=text(sheet_id)
    local sheet=profile_config.sheets[sheet_name]
    front:insert(styled(text(sheet.label)..' — ЗАГЛУШКА','FrontMatterHeading'))
    front:insert(pandoc.Para('Демонстрационный лист. Нормативный образец и состав профиля ещё не утверждены.'))
    if sheet_name=='title' then
      front:insert(styled(value('academic.university'),'Institution'))
      front:insert(styled(value('academic.department'),'Institution'))
      front:insert(styled(text(profile.label),'WorkType'))
      front:insert(styled(value('title'),'Title'))
      front:insert(styled('По дисциплине «'..value('academic.discipline')..'»','TitleDetails'))
      front:insert(styled('Выполнил: '..value('author')..', группа '..value('academic.group'),'StudentDetails'))
      front:insert(styled('Руководитель: '..value('academic.supervisor'),'StudentDetails'))
      front:insert(styled(value('academic.city')..' — '..value('academic.year'),'TitlePlace'))
    elseif sheet_name=='assignment' then
      front:insert(pandoc.Para('Тема: '..value('title')))
      front:insert(pandoc.Para('Исполнитель: '..value('author')))
      front:insert(pandoc.Para('Руководитель: '..value('academic.supervisor')))
      front:insert(pandoc.Para('Содержание задания, сроки и подписи будут добавлены после проверки нормативного образца.'))
    elseif sheet_name=='annotation' then
      front:insert(pandoc.Para(value('title')..'. '..value('academic.summary')))
      front:insert(pandoc.Para({pandoc.Str('Объём работы: '),
        pandoc.RawInline('openxml',field('NUMPAGES')),pandoc.Str(' с.')}))
    else
      assert(false,'No sheet implementation: '..sheet_name)
    end
    front:insert(pagebreak())
  end
  quarto.log.warning('Academic profile "'..id..'" uses placeholder sheets; STO compliance is not confirmed.')
  return front
end
local appendix_ids={}
local function process(doc)
  doc.meta['academic-front-matter']=pandoc.MetaBlocks(front_matter(doc))
  local headings={}
  local numbered_chapters={}
  local marked_appendices={}
  local appendix_started=false
  doc:walk({Header=function(h)
    if h.classes:includes('appendix') then
      assert(h.level==1,'DOCX appendix must use a top-level heading')
      assert(not h.classes:includes('unnumbered'),'Do not add .unnumbered to an .appendix heading')
      appendix_started=true
      marked_appendices[#marked_appendices+1]=h
    elseif appendix_started and h.level==1 then
      assert(false,'All top-level sections after the first appendix must also use .appendix')
    end
    if h.classes:includes('unnumbered') then
      table.insert(headings,{id=h.identifier,unlisted=h.classes:includes('unlisted')})
    elseif h.level==1 then
      table.insert(numbered_chapters,h)
    end
  end})
  -- Keep Book's native chapter registry. A single-file document instead uses
  -- Quarto's documented .appendix heading class (native support is HTML-only).
  -- Both paths feed the same Word layout and object-numbering implementation.
  local files=doc.meta.book and doc.meta.book.appendices or {}
  local appendix_headers=marked_appendices
  if #files>0 then
    assert(#marked_appendices==0,'Use book.appendices or .appendix headings, not both')
    assert(#files<=#numbered_chapters,'Book appendix has no numbered top-level heading')
    appendix_headers={}
    for i=1,#files do
      appendix_headers[#appendix_headers+1]=numbered_chapters[#numbered_chapters-#files+i]
    end
  end
  local letters={'А','Б','В','Г','Д','Е','Ж','И','К','Л','М','Н','П','Р','С','Т','У','Ф','Х','Ц','Ш','Щ','Э','Ю','Я'}
  assert(#appendix_headers<=#letters,'Too many appendices for the STO 04-2008 Cyrillic alphabet')
  local appendices={}
  for i,h in ipairs(appendix_headers) do
    assert(h.identifier~='','Appendix needs a Quarto heading identifier')
    appendices[#appendices+1]={id=h.identifier,letter=letters[i],latin=string.char(64+i),title=text(h)}
    appendix_ids[h.identifier]=letters[i]
  end
  -- Pandoc runs beside the source file. The public relative offset also avoids
  -- converting a non-ASCII Windows project path through Lua's environment.
  local scratch=pandoc.path.join({quarto.project.offset or '.','.quarto'})
  pandoc.system.make_directory(scratch,true)
  local out=assert(io.open(pandoc.path.join({scratch,'academic-headings.json'}),'wb'))
  out:write(quarto.json.encode(headings));out:close()
  local appendix_out=assert(io.open(pandoc.path.join({scratch,'academic-appendices.json'}),'wb'))
  appendix_out:write(quarto.json.encode(appendices));appendix_out:close()
  return doc
end
local function para(p)
  local id=text(p):match('{#(eq%-[%w-]+)}$')
  if id then
    for _,inline in ipairs(p.content) do
      if inline.t=='Math' then
        return {pandoc.Para({inline}),pandoc.Para({number(id,'Формула','(',')')})}
      end
    end
  end
end
local function cite(c)
  if #c.citations==1 then
    local id=c.citations[1].id
    if appendix_ids[id] then
      local caption='приложение '..appendix_ids[id]
      return pandoc.RawInline('openxml','<w:hyperlink w:anchor="'..esc(bookmark(id))..'"><w:r><w:rPr><w:rStyle w:val="Hyperlink"/></w:rPr><w:t>'..esc(caption)..'</w:t></w:r></w:hyperlink>')
    end
    if id:match('^fig%-') or id:match('^tbl%-') or id:match('^eq%-') or id:match('^sec%-') then
      local reference=field('REF '..bookmark(id)..(id:match('^sec%-') and ' \\n' or '')..' \\h')
      if id:match('^eq%-') and c.citations[1].mode~='SuppressAuthor' then
        reference='<w:r><w:t>(</w:t></w:r>'..reference..'<w:r><w:t>)</w:t></w:r>'
      end
      return pandoc.RawInline('openxml',reference)
    end
  end
end
return {{Pandoc=process},{Para=para},{Cite=cite}}
