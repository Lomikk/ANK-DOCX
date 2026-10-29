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
local appendix_ids={}
local function process(doc)
  local meta=doc.meta.academic or {}
  local front=pandoc.Blocks({})
  local function value(key) return text(meta[key] or '') end
  for _,key in ipairs({'university','department','work-type','discipline','group','supervisor','city','year'}) do
    if value(key)=='' then error('Missing academic metadata: '..key) end
  end
  front:insert(styled(value('university'),'Institution'))
  front:insert(styled(value('department'),'Institution'))
  front:insert(styled(value('work-type'),'WorkType'))
  front:insert(styled(text(doc.meta.title),'Title'))
  front:insert(styled('По дисциплине «'..value('discipline')..'»','TitleDetails'))
  local author=text(doc.meta.author)
  front:insert(styled('Выполнил: '..author..', группа '..value('group'),'StudentDetails'))
  front:insert(styled('Руководитель: '..value('supervisor'),'StudentDetails'))
  front:insert(styled(value('city')..' — '..value('year'),'TitlePlace'))
  front:insert(pagebreak())
  if meta.annotation ~= false then
    front:insert(styled('Аннотация','FrontMatterHeading'))
    front:insert(pandoc.Para(text(doc.meta.title)..'. '..value('summary')))
    front:insert(pandoc.Para({pandoc.Str('Объём работы: '),pandoc.RawInline('openxml',field('NUMPAGES')),pandoc.Str(' с.')}))
    front:insert(pagebreak())
  end
  doc.meta['academic-front-matter']=pandoc.MetaBlocks(front)
  local headings={}
  local numbered_chapters={}
  doc:walk({Header=function(h)
    if h.classes:includes('unnumbered') then
      table.insert(headings,{id=h.identifier,unlisted=h.classes:includes('unlisted')})
    elseif h.level==1 then
      table.insert(numbered_chapters,h)
    end
  end})
  -- Quarto Book already owns book.appendices and the assembled chapter order.
  -- Its final N numbered H1 blocks are the N appendix titles. No separate
  -- author-side registry or manual appendix labels are necessary.
  local files=doc.meta.book and doc.meta.book.appendices or {}
  local letters={'А','Б','В','Г','Д','Е','Ж','И','К','Л','М','Н','П','Р','С','Т','У','Ф','Х','Ц','Ш','Щ','Э','Ю','Я'}
  if #files>#letters then error('Too many appendices for the STO 04-2008 Cyrillic alphabet') end
  if #files>#numbered_chapters then error('Book appendix has no numbered top-level heading') end
  local appendices={}
  for i=1,#files do
    local h=numbered_chapters[#numbered_chapters-#files+i]
    if h.identifier=='' then error('Appendix needs a Quarto heading identifier') end
    appendices[#appendices+1]={id=h.identifier,letter=letters[i],latin=string.char(64+i),title=text(h)}
    appendix_ids[h.identifier]=letters[i]
  end
  local out=assert(io.open('.quarto/academic-headings.json','wb'))
  out:write(quarto.json.encode(headings));out:close()
  local appendix_out=assert(io.open('.quarto/academic-appendices.json','wb'))
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
      if id:match('^eq%-') then
        reference='<w:r><w:t>(</w:t></w:r>'..reference..'<w:r><w:t>)</w:t></w:r>'
      end
      return pandoc.RawInline('openxml',reference)
    end
  end
end
return {{Pandoc=process},{Para=para},{Cite=cite}}
