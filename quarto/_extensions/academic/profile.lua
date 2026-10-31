-- Fixed supported bundles, read by Lua and the workspace CLI with native JSON APIs.
local M = {}

function M.load(directory)
  local file = assert(io.open(pandoc.path.join({directory, 'profiles.json'}), 'rb'))
  local config = quarto.json.decode(file:read('*a'))
  file:close()
  return config
end

function M.resolve(config, id, standard)
  local alias = config.aliases[id]
  local selected = id
  if alias then
    selected = standard == '' and alias.default or alias.standards[standard]
    assert(selected, 'Unsupported academic.standard / academic.profile combination: '..standard..' / '..id)
  end
  local profile = config.profiles[selected]
  local names = {}
  for name in pairs(config.profiles) do names[#names+1] = name end
  table.sort(names)
  assert(profile, 'Unknown or missing academic.profile: "'..id..'". Available profiles: '..table.concat(names, ', '))
  assert(standard == '' or standard == profile.standard,
    'academic.standard conflicts with academic.profile: '..standard..' / '..selected)
  return profile, selected
end

return M
