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
  doc:walk({Header=function(h)
    if h.classes:includes('unnumbered') then
      table.insert(headings,{id=h.identifier,unlisted=h.classes:includes('unlisted')})
    end
  end})
  local out=assert(io.open('.quarto/academic-headings.json','wb'))
  out:write(quarto.json.encode(headings));out:close()
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
