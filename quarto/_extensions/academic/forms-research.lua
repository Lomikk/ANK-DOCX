-- STO 19 forms transferred from the original ANK; one-volume explanatory note.
-- Specific wording stays here; ordinary Pandoc layout and counters are shared.
local text = pandoc.utils.stringify
return function(c)
  local p, tab, v = c.p, c.tab, c.v
  local function signatures(left, right)
    tab(left, right)
    tab(c.date, c.date)
  end
  return {
    ['research-title-19'] = function()
      c.institution(false)
      p('УДК '..v('udc')) -- 5.1.2, even though the Appendix B picture omits it.
      tab('РАБОТА ПРОВЕРЕНА', 'ДОПУСТИТЬ К ЗАЩИТЕ')
      tab('Рецензент, '..v('reviewer.position'), v('assignment.head-of-department-position'))
      signatures('____________ '..v('reviewer.name'), '____________ '..v('assignment.head-of-department'))
      p(c.title, 'ResearchTitle')
      p({pandoc.Str('ПОЯСНИТЕЛЬНАЯ ЗАПИСКА'), pandoc.LineBreak(), pandoc.Str('К ВЫПУСКНОЙ КВАЛИФИКАЦИОННОЙ НИР')}, 'WorkType')
      p(v('document-code'), 'Centered')
      local consultants = c.meta.academic.consultants or {}
      for _, consultant in ipairs(consultants) do
        assert(c.value(consultant, 'name') ~= '' and c.value(consultant, 'position') ~= '',
          'academic.consultants rows require name and position')
        tab('Консультант, '..c.value(consultant, 'position'), '')
        tab('____________ '..c.value(consultant, 'name'), '')
        tab(c.date, '')
      end
      p('Руководитель НИР, '..v('supervisor-position'), 'RightBlockStart')
      p('____________ '..v('supervisor'), 'RightBlock')
      p(c.date, 'RightBlock')
      p('Автор НИР, студент группы '..v('group'), 'RightBlockStart')
      p('____________ '..c.author, 'RightBlock')
      p(c.date, 'RightBlock')
      p('Нормоконтролер, '..v('normal-controller.position'), 'RightBlockStart')
      p('____________ '..v('normal-controller.name'), 'RightBlock')
      p(c.date, 'RightBlock')
      p(v('city')..' '..v('year'), 'TitlePlace')
      c.mark('AcademicTitleEnd')
    end,
    ['research-assignment-19'] = function()
      c.assignment_data()
      c.institution(false)
      p('Специальность «'..v('direction-code')..' '..v('direction')..'»', 'Institution')
      p('УТВЕРЖДАЮ', 'ApprovalStart')
      p(v('assignment.head-of-department-position'), 'RightBlock')
      p('____________ '..v('assignment.head-of-department'), 'RightBlock')
      p(c.date, 'RightBlock')
      p('ЗАДАНИЕ', 'SheetHeading')
      p('на выпускную квалификационную НИР студента', 'Centered')
      p(c.full_name..', группа '..v('group'), 'StudentName')
      p('Тема НИР: '..c.title, 'FormItem')
      p('Документ об утверждении темы: '..v('assignment.approval-document'), 'FormItem')
      p('Плановый срок сдачи: '..v('assignment.due-date'), 'FormItem')
      p('Исходные данные к НИР: '..v('assignment.source-data'), 'FormItem')
      p('Перечень вопросов, подлежащих разработке:', 'FormItem')
      for i, question in ipairs(c.questions) do p(tostring(i)..'. '..text(question)) end
      p('Иллюстративный материал: '..v('assignment.illustrations'), 'FormItem')
      p('Календарный план', 'FormItem')
      for i, row in ipairs(c.calendar) do p(tostring(i)..'. '..c.value(row, 'section')..' — '..c.value(row, 'due')) end
      p('Руководитель НИР: ____________ '..v('supervisor'), 'FormItem')
      p('Студент: ____________ '..c.author)
      p(v('assignment.head-of-department-position')..': ____________ '..v('assignment.head-of-department'))
      c.mark('AcademicAssignmentEnd') -- Flow naturally; no fixed two-page assumption.
    end,
    ['research-summary-19'] = function()
      local keywords = c.meta.academic.keywords or {}
      assert(#keywords >= 5 and #keywords <= 15, 'academic.keywords must contain 5–15 entries')
      local words = {}
      for _, keyword in ipairs(keywords) do
        local word = text(keyword)
        assert(not word:match('^%s*$') and word == pandoc.text.lower(word),
          'academic.keywords must contain nonempty lowercase entries')
        words[#words+1] = word
      end
      p('РЕФЕРАТ', 'SummaryHeading')
      local summary = c.statistics()
      summary[#summary+1] = pandoc.Str(' 1 ч.')
      p(summary, 'AnnotationReference')
      p('Ключевые слова: '..table.concat(words, ', ')..'.', 'FormItem')
      p(v('summary'), 'AnnotationText')
    end
  }
end
