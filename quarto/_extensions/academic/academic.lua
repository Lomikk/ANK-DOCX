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
local extension_dir=pandoc.path.directory(PANDOC_SCRIPT_FILE)
local render_front_matter=dofile(pandoc.path.join({extension_dir,'front-matter.lua'}))
local appendix_ids={}
local function process(doc)
  doc.meta['academic-front-matter']=pandoc.MetaBlocks(render_front_matter(doc,field,extension_dir))
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
