-- Standard Pandoc blocks and custom styles; the SDK adapter supplies page sections.
-- Forms: STO 21-2008 A1/V, 7.3; STO 17-2008 G, 4.2.2.2.
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
local function marker(style)
  return pandoc.RawBlock('openxml', '<w:p><w:pPr><w:pStyle w:val="'..style..'"/></w:pPr></w:p>')
end
return function(doc, field, directory)
  local file = assert(io.open(pandoc.path.join({directory, 'profiles.yml'}), 'rb'))
  local config = pandoc.read('---\n'..file:read('*a')..'\n---\n', 'markdown').meta
  file:close()
  local id = value(doc.meta, 'academic.profile')
  local profile = config.profiles[id]
  assert(profile, 'Unknown or missing academic.profile: "'..id..'". Available profiles: coursework, referat')
  local standard = value(doc.meta, 'academic.standard')
  if standard == '' then standard = id == 'coursework' and 'susu-21-2008' or 'susu-17-2008' end
  assert(id == 'coursework' and (standard == 'susu-04-2008' or standard == 'susu-21-2008') or
    id == 'referat' and standard == 'susu-17-2008', 'Unsupported academic.standard / academic.profile combination: '..standard..' / '..id)
  local missing = {}
  for _, sheet_id in ipairs(profile['before-body']) do
    local sheet = assert(config.sheets[text(sheet_id)])
    for _, required in ipairs(sheet.required or {}) do
      local path = text(required)
      if value(doc.meta, path):match('^%s*$') then missing[path] = true end
    end
  end
  for _, required in ipairs(profile.required or {}) do
    local path = text(required)
    if value(doc.meta, path):match('^%s*$') then missing[path] = true end
  end
  local paths = {}
  for path in pairs(missing) do paths[#paths+1] = path end
  table.sort(paths)
  assert(#paths == 0, 'Missing metadata for academic.profile "'..id..'": '..table.concat(paths, ', '))
  local function v(path) return value(doc.meta, 'academic.'..path) end
  local scratch = pandoc.path.join({quarto.project.offset or '.', '.quarto'})
  pandoc.system.make_directory(scratch, true)
  local layout = assert(io.open(pandoc.path.join({scratch, 'academic-layout.json'}), 'wb'))
  layout:write(quarto.json.encode({Standard=standard, Profile=id, Title=value(doc.meta,'title'), DocumentCode=v('document-code'),
    Author=v('author-short'), Supervisor=v('supervisor'), NormalController=v('normal-controller.name'),
    Approver=v('stamp.approver'), Organization=v('stamp.organization') ~= '' and v('stamp.organization') or v('university-short')..'\n'..v('group')}))
  layout:close()
  local questions = get(doc.meta, 'academic.assignment.questions')
  local calendar = get(doc.meta, 'academic.assignment.calendar')
  if id == 'coursework' then
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
  local out = pandoc.Blocks({})
  local function p(content, style)
    out:insert(pandoc.Div({pandoc.Para(content)}, {['custom-style'] = 'Academic'..(style or 'FormText')}))
  end
  local function tab(left, right)
    p({pandoc.Str(left), pandoc.RawInline('openxml','<w:r><w:tab/></w:r>'), pandoc.Str(right)}, 'SignColumns')
  end
  local function institution(direction)
    p(v('parent-organization'), 'Institution')
    p(v('university'), 'Institution')
    p(v('school'), 'Institution')
    p('Кафедра «'..v('department')..'»', 'Institution')
    if direction then p('Направление «'..v('direction-code')..' '..v('direction')..'»', 'Institution') end
  end
  local date = '«_____» ______________ '..v('year')..' г.'
  local author = v('author-short')
  local renderers = {}
  renderers.title = function()
  institution(false)
  p(value(doc.meta, 'title'), id == 'coursework' and 'CourseworkTitle' or 'ReferatTitle')
  if standard == 'susu-04-2008' then
    p({pandoc.Str('ПОЯСНИТЕЛЬНАЯ ЗАПИСКА'), pandoc.LineBreak(), pandoc.Str('К КУРСОВОЙ РАБОТЕ')}, 'WorkType')
  else
    p(id == 'coursework' and 'ПОЯСНИТЕЛЬНАЯ ЗАПИСКА К КУРСОВОЙ РАБОТЕ' or 'РЕФЕРАТ', 'WorkType')
  end
  p('по дисциплине «'..v('discipline')..'»', 'Centered')
  if id == 'coursework' then p(v('document-code'), 'Centered') end
  if id == 'coursework' then
    tab('Нормоконтролер,', 'Руководитель,')
    tab(v('normal-controller.position'), v('supervisor-position'))
    tab('____________ '..v('normal-controller.name'), '____________ '..v('supervisor'))
    tab(date, date)
  else
    p('Проверил, '..v('supervisor-position'), 'RightBlock')
    p('____________ '..v('supervisor'), 'RightBlock')
    p(date, 'RightBlock')
  end
  p('Автор работы', 'RightBlockStart')
  p('Студент группы '..v('group'), 'RightBlock')
  p('____________ '..author, 'RightBlock')
  p(date, 'RightBlock')
  p(id == 'coursework' and 'Работа защищена' or 'Реферат защищён', 'RightBlockStart')
  p('с оценкой (прописью, цифрой)', 'RightBlock')
  p('____________________________', 'RightBlock')
  p(date, 'RightBlock')
  p(v('city')..' '..v('year'), 'TitlePlace')
  out:insert(marker('AcademicTitleEnd'))
  end
  renderers.assignment = function()
    institution(true)
    p('УТВЕРЖДАЮ', 'ApprovalStart')
    p('Заведующий кафедрой', 'RightBlock')
    p('____________ '..v('assignment.head-of-department'), 'RightBlock')
    p(v('assignment.approval-date'), 'RightBlock')
    p('ЗАДАНИЕ', 'SheetHeading')
    p('на курсовую работу студента', 'Centered')
    p(value(doc.meta, 'author'), 'StudentName')
    p('(фамилия, имя, отчество полностью)', 'Centered')
    p('Группа '..v('group'), 'Centered')
    p('1 Дисциплина: '..v('discipline'), 'FormItem')
    p('2 Тема работы: '..value(doc.meta, 'title'), 'FormItem')
    p('3 Срок сдачи студентом законченной работы: '..v('assignment.due-date'), 'FormItem')
    p('4 Перечень вопросов, подлежащих разработке', 'FormItem')
    for i, question in ipairs(questions) do p(tostring(i)..'. '..text(question)) end
    out:insert(marker('AcademicAssignmentPageEnd'))
    p('Продолжение задания на курсовую работу', 'Centered')
    p('5 Календарный план', 'FormItem')
    local function cell(s) return {pandoc.Plain(s)} end
    local rows = {}
    for _, row in ipairs(calendar) do
      rows[#rows+1] = {cell(value(row, 'section')), cell(value(row, 'due')), cell(value(row, 'status'))}
    end
    out:insert(pandoc.utils.from_simple_table(pandoc.SimpleTable({},
      {pandoc.AlignLeft, pandoc.AlignCenter, pandoc.AlignCenter}, {0.50, 0.24, 0.26},
      {cell('Наименование разделов курсовой работы'), cell('Срок выполнения'), cell('Отметка руководителя')}, rows)))
    p('Руководитель работы ____________ '..v('supervisor')..'   '..date, 'FormSignature')
    p('Студент ____________ '..author..'   '..date, 'FormText')
    out:insert(marker('AcademicAssignmentEnd'))
  end
  renderers.annotation = function()
  p('АННОТАЦИЯ', 'SheetHeading')
  local summary = {pandoc.Str(author..' '..value(doc.meta, 'title')..'. — '..v('city')..': '..v('university-short')..', '..v('group')..', '..v('year')..'. — '),
    pandoc.RawInline('openxml', field('NUMPAGES')), pandoc.Str(' с., ')}
  for _, counter in ipairs({{'Figures',' ил., '}, {'Tables',' табл., библиогр. список — '},
      {'Sources',' наим., '}, {'Appendices',' прил.'}}) do
    summary[#summary+1] = pandoc.RawInline('openxml', field('DOCPROPERTY Academic'..counter[1]))
    summary[#summary+1] = pandoc.Str(counter[2])
  end
  p(summary, 'AnnotationReference')
  p(v('summary'), 'AnnotationText')
  end
  for _, sheet_id in ipairs(profile['before-body']) do
    assert(renderers[text(sheet_id)], 'No sheet implementation: '..text(sheet_id))()
  end
  return out
end
