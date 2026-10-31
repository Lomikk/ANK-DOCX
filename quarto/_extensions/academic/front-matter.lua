-- Resolve one supported bundle; concrete forms generate ordinary Pandoc blocks.
local text = pandoc.utils.stringify
local function get(meta, path)
  local value = meta
  for key in path:gmatch('[^.]+') do
    if type(value) ~= 'table' then return nil end
    value = value[key]
  end
  return value
end
local function value(meta, path) return text(get(meta, path) or '') end

return function(doc, field, directory)
  local profiles = dofile(pandoc.path.join({directory, 'profile.lua'}))
  local config = profiles.load(directory)
  local profile, id = profiles.resolve(config, value(doc.meta, 'academic.profile'), value(doc.meta, 'academic.standard'))
  local missing = {}
  for _, group in ipairs(profile.requires) do
    for _, path in ipairs(assert(config.requirements[group], 'Unknown metadata group: '..group)) do
      if value(doc.meta, path):match('^%s*$') then missing[path] = true end
    end
  end
  local paths = {}
  for path in pairs(missing) do paths[#paths+1] = path end
  table.sort(paths)
  assert(#paths == 0, 'Missing metadata for academic.profile "'..id..'": '..table.concat(paths, ', '))

  local function v(path) return value(doc.meta, 'academic.'..path) end
  local questions = get(doc.meta, 'academic.assignment.questions')
  local calendar = get(doc.meta, 'academic.assignment.calendar')
  local render = dofile(pandoc.path.join({directory, 'forms.lua'}))
  local blocks = render(doc, profile.forms, field, value, questions, calendar, directory)
  local scratch = pandoc.path.join({quarto.project.offset or '.', '.quarto'})
  pandoc.system.make_directory(scratch, true)
  local layout = assert(io.open(pandoc.path.join({scratch, 'academic-layout.json'}), 'wb'))
  layout:write(quarto.json.encode({Standard=profile.standard, Profile=id, PageLayout=profile['page-layout'],
    FigureNumbering=profile['figure-numbering'] or 'continuous',
    Title=value(doc.meta,'title'), DocumentCode=v('document-code'), Author=v('author-short'), Supervisor=v('supervisor'),
    NormalController=v('normal-controller.name'), Approver=v('stamp.approver'),
    Organization=v('stamp.organization') ~= '' and v('stamp.organization') or v('university-short')..'\n'..v('group')}))
  layout:close()
  return blocks
end
