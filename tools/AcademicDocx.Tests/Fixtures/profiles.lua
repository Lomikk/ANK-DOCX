-- Run with Quarto's Pandoc and --metadata-file Workspaces/Пример/metadata.yml.
-- Exercises real Pandoc blocks and native JSON without R/Word or full rendering.
quarto = {json = pandoc.json, project = {offset = '.'}}
local directory = assert(os.getenv('ACADEMIC_PROFILE_EXTENSION'))
local render = dofile(pandoc.path.join({directory, 'front-matter.lua'}))
local profiles = dofile(pandoc.path.join({directory, 'profile.lua'}))
local config = profiles.load(directory)
local function field(code) return '<w:r><w:t>'..code..'</w:t></w:r>' end

function Pandoc(doc)
  local meta = doc.meta
  local function blocks(id, standard)
    meta.academic.profile = pandoc.MetaString(id)
    meta.academic.standard = pandoc.MetaString(standard or '')
    return render(pandoc.Pandoc({}, meta), field, directory)
  end
  for id, profile in pairs(config.profiles) do
    local explicit = blocks(id)
    local legacy = blocks(profile.kind, profile.standard)
    assert(pandoc.write(pandoc.Pandoc(explicit), 'json') == pandoc.write(pandoc.Pandoc(legacy), 'json'),
      'Legacy and explicit forms differ: '..id)
    local plain = pandoc.write(pandoc.Pandoc(explicit), 'plain')
    assert(plain:find('АННОТАЦИЯ', 1, true))
    assert((plain:find('ЗАДАНИЕ', 1, true) ~= nil) == (profile.kind == 'coursework'))
    local file = assert(io.open('.quarto/academic-layout.json', 'rb'))
    local layout = pandoc.json.decode(file:read('*a')); file:close()
    assert(layout.Profile == id and layout.Standard == profile.standard and layout.PageLayout == profile['page-layout'])
  end
  for _, pair in ipairs({{'referat', 'susu-04-2008'}, {'referat-susu-17-2008', 'susu-04-2008'}, {'missing-profile', ''}}) do
    assert(not pcall(blocks, pair[1], pair[2]), 'Unsupported combination accepted')
  end
  local summary = meta.academic.summary
  meta.academic.summary = nil
  local ok, err = pcall(blocks, 'referat-susu-17-2008')
  assert(not ok and tostring(err):find('academic.summary', 1, true))
  meta.academic.summary = summary
  return pandoc.Pandoc({pandoc.Para('Profile selection and concrete forms passed')})
end
