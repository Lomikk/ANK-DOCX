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
  -- Additional fictitious metadata needed only by the new research forms.
  meta.academic.udc = pandoc.MetaString('004.02')
  meta.academic.reviewer = pandoc.MetaMap({name=pandoc.MetaString('Р.Р. Рецензентов'), position=pandoc.MetaString('доцент')})
  meta.academic.assignment['head-of-department-position'] = pandoc.MetaString('Заведующий кафедрой')
  meta.academic.assignment['approval-document'] = pandoc.MetaString('Учебный пример приказа')
  meta.academic.assignment['source-data'] = pandoc.MetaString('Параметры модели')
  meta.academic.assignment.illustrations = pandoc.MetaString('Графики модели')
  meta.academic.keywords = pandoc.MetaList({pandoc.MetaString('модель'), pandoc.MetaString('расчёт'),
    pandoc.MetaString('данные'), pandoc.MetaString('алгоритм'), pandoc.MetaString('результат')})
  local function blocks(id, standard)
    meta.academic.profile = pandoc.MetaString(id)
    meta.academic.standard = pandoc.MetaString(standard or '')
    return render(pandoc.Pandoc({}, meta), field, directory)
  end
  for id, profile in pairs(config.profiles) do
    local explicit = blocks(id)
    if config.aliases[profile.kind] then
      local legacy = blocks(profile.kind, profile.standard)
      assert(pandoc.write(pandoc.Pandoc(explicit), 'json') == pandoc.write(pandoc.Pandoc(legacy), 'json'),
        'Legacy and explicit forms differ: '..id)
    end
    local plain = pandoc.write(pandoc.Pandoc(explicit), 'plain')
    assert(plain:find(profile.kind == 'research' and 'РЕФЕРАТ' or 'АННОТАЦИЯ', 1, true))
    assert((plain:find('ЗАДАНИЕ', 1, true) ~= nil) == (profile.kind ~= 'referat'))
    if profile.kind == 'research' then
      assert(plain:find('004.02', 1, true) and plain:find('Исходные данные', 1, true) and plain:find('Ключевые слова', 1, true))
    end
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
  meta.academic.keywords = pandoc.MetaList({pandoc.MetaString('один')})
  local keywords_ok, keywords_error = pcall(blocks, 'research-susu-19-2008')
  assert(not keywords_ok and tostring(keywords_error):find('academic.keywords', 1, true))
  return pandoc.Pandoc({pandoc.Para('Profile selection and concrete forms passed')})
end
