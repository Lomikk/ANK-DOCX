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
  if profile.kind == 'coursework' then
    assert(pandoc.utils.type(questions) == 'List' and #questions > 0,
      'academic.assignment.questions must be a nonempty YAML list')
    assert(pandoc.utils.type(calendar) == 'List' and #calendar > 0,
      'academic.assignment.calendar must be a nonempty YAML list')
    for _, question in ipairs(questions) do
      assert(not text(question):match('^%s*$'), 'academic.assignment.questions contains an empty question')
    end
    for _, row in ipairs(calendar) do
      assert(value(row, 'section') ~= '' and value(row, 'due') ~= '',
        'academic.assignment.calendar rows require section and due')
    end
  end

  local render = dofile(pandoc.path.join({directory, 'forms.lua'}))
  local blocks = render(doc, profile.forms, field, value, questions, calendar)
  local scratch = pandoc.path.join({quarto.project.offset or '.', '.quarto'})
  pandoc.system.make_directory(scratch, true)
  local layout = assert(io.open(pandoc.path.join({scratch, 'academic-layout.json'}), 'wb'))
  layout:write(quarto.json.encode({Standard=profile.standard, Profile=id, PageLayout=profile['page-layout'],
    Title=value(doc.meta,'title'), DocumentCode=v('document-code'), Author=v('author-short'), Supervisor=v('supervisor'),
    NormalController=v('normal-controller.name'), Approver=v('stamp.approver'),
    Organization=v('stamp.organization') ~= '' and v('stamp.organization') or v('university-short')..'\n'..v('group')}))
  layout:close()
  return blocks
end
