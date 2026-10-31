-- Concrete supported forms. Shared helpers lay out text; no profile/standard routing here.
local text = pandoc.utils.stringify
local function marker(style)
  return pandoc.RawBlock('openxml', '<w:p><w:pPr><w:pStyle w:val="'..style..'"/></w:pPr></w:p>')
end

return function(doc, forms, field, value, questions, calendar)
  local out = pandoc.Blocks({})
  local function v(path) return value(doc.meta, 'academic.'..path) end
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
  local function title_end(defense)
    p('Автор работы', 'RightBlockStart')
    p('Студент группы '..v('group'), 'RightBlock')
    p('____________ '..author, 'RightBlock')
    p(date, 'RightBlock')
    p(defense, 'RightBlockStart')
    p('с оценкой (прописью, цифрой)', 'RightBlock')
    p('____________________________', 'RightBlock')
    p(date, 'RightBlock')
    p(v('city')..' '..v('year'), 'TitlePlace')
    out:insert(marker('AcademicTitleEnd'))
  end
  local function coursework_title(work_type)
    institution(false)
    p(value(doc.meta, 'title'), 'CourseworkTitle')
    p(work_type, 'WorkType')
    p('по дисциплине «'..v('discipline')..'»', 'Centered')
    p(v('document-code'), 'Centered')
    tab('Нормоконтролер,', 'Руководитель,')
    tab(v('normal-controller.position'), v('supervisor-position'))
    tab('____________ '..v('normal-controller.name'), '____________ '..v('supervisor'))
    tab(date, date)
    title_end('Работа защищена')
  end
  local renderers = {}
  renderers['coursework-title-21'] = function()
    coursework_title('ПОЯСНИТЕЛЬНАЯ ЗАПИСКА К КУРСОВОЙ РАБОТЕ')
  end
  renderers['coursework-title-04'] = function()
    coursework_title({pandoc.Str('ПОЯСНИТЕЛЬНАЯ ЗАПИСКА'), pandoc.LineBreak(), pandoc.Str('К КУРСОВОЙ РАБОТЕ')})
  end
  renderers['referat-title-17'] = function()
    institution(false)
    p(value(doc.meta, 'title'), 'ReferatTitle')
    p('РЕФЕРАТ', 'WorkType')
    p('по дисциплине «'..v('discipline')..'»', 'Centered')
    p('Проверил, '..v('supervisor-position'), 'RightBlock')
    p('____________ '..v('supervisor'), 'RightBlock')
    p(date, 'RightBlock')
    title_end('Реферат защищён')
  end
  renderers['coursework-assignment'] = function()
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
  for _, form in ipairs(forms) do
    assert(renderers[form], 'No sheet implementation: '..form)()
  end
  return out
end
