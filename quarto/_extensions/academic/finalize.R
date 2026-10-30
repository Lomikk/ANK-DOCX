# Standard Quarto project hook. Only missing native Word mechanisms live here.
library(xml2)
W <- "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
ns <- c(w = W, m = "http://schemas.openxmlformats.org/officeDocument/2006/math")
escape <- function(x) {
  x <- gsub("&", "&amp;", as.character(x), fixed = TRUE)
  x <- gsub("<", "&lt;", x, fixed = TRUE)
  gsub('"', "&quot;", x, fixed = TRUE)
}
node <- function(x) read_xml(paste0('<root xmlns:w="', W, '" xmlns:m="', ns[["m"]], '">', x, '</root>')) |> xml_child()
run <- function(x) paste0('<w:r><w:t xml:space="preserve">', escape(x), '</w:t></w:r>')
field <- function(code) paste0('<w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r><w:r><w:instrText xml:space="preserve"> ', escape(code), ' </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r>', run("0"), '<w:r><w:fldChar w:fldCharType="end"/></w:r>')
text <- function(x) paste(xml_text(xml_find_all(x, ".//w:t", ns)), collapse = "")
attr <- function(x, name) xml_attr(x, name, ns = c(w = W))
put <- function(x, name, value) xml_set_attr(x, paste0("w:", name), value, ns = c(w = W))
outputs <- strsplit(Sys.getenv("QUARTO_PROJECT_OUTPUT_FILES"), "\n", fixed = TRUE)[[1]]
outputs <- outputs[grepl("[.]docx$", outputs, ignore.case = TRUE)]
if (!length(outputs)) stop("Academic hook did not receive a DOCX output")

for (output in outputs) {
  output <- normalizePath(output, mustWork = TRUE)
  folder <- tempfile("academic-docx-")
  dir.create(folder)
  unzip(output, exdir = folder)
  # An incompatible SVG converter may exit successfully but emit an empty PNG.
  media <- list.files(file.path(folder, "word/media"), full.names = TRUE)
  if (length(media) && any(file.info(media)$size == 0)) stop("Empty embedded image: check the SVG converter; this DOCX is not ready")
  doc <- read_xml(file.path(folder, "word/document.xml"))
  # Quarto also wraps captioned listings in a one-cell table. They are ordinary
  # Word paragraphs after flattening, with no artificial table around the code.
  wrappers <- xml_find_all(doc, "//w:tbl[count(w:tr)=1 and count(w:tr/w:tc)=1][w:tr/w:tc/w:bookmarkStart[starts-with(@w:name,'tbl-') or starts-with(@w:name,'fig-') or starts-with(@w:name,'lst-')]]", ns)
  for (wrapper in wrappers) {
    listing <- length(xml_find_all(wrapper, "./w:tr/w:tc/w:bookmarkStart[starts-with(@w:name,'lst-')]", ns)) > 0
    if (listing) {
      # Quarto centres both the ImageCaption and the SourceCode paragraph inside
      # its one-cell table. Align only listing paragraphs; figures keep their style.
      paragraphs <- xml_find_all(wrapper, "./w:tr/w:tc/w:p", ns)
      if (!length(paragraphs)) stop("Captioned listing has no caption paragraph")
      for (paragraph in paragraphs) {
        pr <- xml_find_first(paragraph, "./w:pPr", ns)
        if (inherits(pr, "xml_missing")) pr <- xml_add_child(paragraph, node("<w:pPr/>"), .where = 0)
        xml_remove(xml_find_all(pr, "./w:jc|./w:ind", ns))
        xml_add_child(pr, node('<w:ind w:firstLine="0" w:left="0" w:right="0"/>'))
        xml_add_child(pr, node('<w:jc w:val="left"/>'))
      }
      caption_pr <- xml_find_first(paragraphs[[1]], "./w:pPr", ns)
      if (!length(xml_find_all(caption_pr, "./w:keepNext", ns))) {
        style <- xml_find_first(caption_pr, "./w:pStyle", ns)
        if (inherits(style, "xml_missing")) {
          xml_add_child(caption_pr, node("<w:keepNext/>"), .where = 0)
        } else {
          xml_add_sibling(style, node("<w:keepNext/>"), .where = "after")
        }
      }
    }
    content <- xml_find_all(wrapper, "./w:tr/w:tc/*[not(self::w:tcPr)]", ns)
    for (part in content) xml_add_sibling(wrapper, part, .where = "before")
    xml_remove(wrapper)
  }
  # Preserve unnumbered book headings despite numbering inherited from Word styles.
  headings <- jsonlite::fromJSON(".quarto/academic-headings.json", simplifyVector = FALSE)
  for (heading in headings) {
    starts <- xml_find_all(doc, "//w:bookmarkStart", ns)
    matching <- starts[vapply(starts, attr, "", name="w:name") == heading$id]
    if (!length(matching)) next
    p <- xml_find_first(matching[[1]], "following::w:p[1]", ns)
    if (inherits(p,"xml_missing")) next
    if (!nzchar(text(p))) { xml_remove(p); next }
    pr <- xml_find_first(p,"./w:pPr",ns)
    if (inherits(pr,"xml_missing")) pr <- xml_add_child(p,node("<w:pPr/>"),.where=0)
    # Structural headings share the first outline level but have no number
    # and are centred. Lower unnumbered headings retain their own level.
    heading_style <- xml_find_first(pr, "./w:pStyle", ns)
    if (!inherits(heading_style, "xml_missing") && identical(attr(heading_style, "w:val"), "Heading1")) {
      put(heading_style, "val", "UnnumberedHeading")
    }
    xml_remove(xml_find_all(pr,"./w:numPr",ns))
    xml_add_child(pr,node('<w:numPr><w:numId w:val="0"/></w:numPr>'))
    if (isTRUE(heading$unlisted)) xml_add_child(pr,node('<w:outlineLvl w:val="9"/>'))
  }
  # The Lua filter emits appendix headings from Book or standard .appendix
  # classes. Both authoring paths share this Word layout implementation.
  appendices <- jsonlite::fromJSON(".quarto/academic-appendices.json",simplifyVector=FALSE)
  appendix_headings <- list()
  for (appendix in appendices) {
    mark <- xml_find_first(doc,paste0("//w:bookmarkStart[@w:name='",appendix$id,"']"),ns)
    if (inherits(mark,"xml_missing")) stop("Missing appendix bookmark: ",appendix$id)
    p <- xml_find_first(mark,"following::w:p[1]",ns)
    style <- xml_find_first(p,"./w:pPr/w:pStyle",ns)
    if (inherits(style,"xml_missing") || attr(style,"w:val")!="Heading1")
      stop("Appendix must begin with a top-level heading: ",appendix$id)
    appendix_headings[[length(appendix_headings)+1L]] <- c(list(p=p),appendix)
    pr <- xml_find_first(p,"./w:pPr",ns)
    xml_remove(xml_find_all(pr,"./w:numPr|./w:jc|./w:pageBreakBefore",ns))
    xml_add_child(pr,node('<w:numPr><w:numId w:val="0"/></w:numPr>'))
    xml_add_child(pr,node('<w:jc w:val="center"/>'))
    xml_add_child(pr,node('<w:pageBreakBefore/>'))
    xml_remove(xml_find_all(p,"./w:r|./w:hyperlink",ns))
    xml_add_child(p,node(run(paste0("ПРИЛОЖЕНИЕ ",appendix$letter))))
    xml_add_child(p,node('<w:r><w:br/></w:r>'))
    # Heading1 uses all-caps for chapter titles. The thematic appendix title
    # must retain its sentence case on the second line (§4.14).
    xml_add_child(p,node(paste0('<w:r><w:rPr><w:caps w:val="0"/></w:rPr><w:t xml:space="preserve">',escape(appendix$title),'</w:t></w:r>')))
  }
  bookmarks <- xml_find_all(doc, "//w:bookmarkStart", ns)
  next_id <- max(c(0, as.integer(vapply(bookmarks, attr, "", name = "w:id"))), na.rm = TRUE) + 1
  for (bookmark in bookmarks) {
    name <- attr(bookmark, "w:name")
    if (!grepl("^(sec|fig|tbl|eq)-", name)) next
    normalized <- gsub("[^A-Za-z0-9_]", "_", name)
    put(bookmark, "name", normalized)
    if (!grepl("^(fig|tbl)-", name)) next
    label <- if (startsWith(name, "fig-")) "Рисунок" else "Таблица"
    candidates <- xml_find_all(bookmark, "following::w:p", ns)
    captions <- candidates[startsWith(vapply(candidates, text, ""), label)]
    if (!length(captions)) stop("Missing caption for ", name)
    caption <- captions[[1]]
    caption_text <- sub(paste0("^", label, "[[:space:]\u00a0]+(?:[A-ZА-Я]\\.)?[0-9.]+[[:space:]\u00a0]*[—–:.-]?[[:space:]]*"), "", text(caption), perl = TRUE)
    if (!nzchar(caption_text)) stop("Empty caption for ", name)
    appendix_heading <- xml_find_first(caption,"preceding::w:p[w:pPr/w:pStyle[@w:val='Heading1']][1]",ns)
    appendix <- if (inherits(appendix_heading,"xml_missing")) list() else
      Filter(function(a) identical(xml_path(a$p),xml_path(appendix_heading)),appendix_headings)
    appendix_prefix <- if (length(appendix)) paste0(appendix[[1]]$letter,".") else ""
    sequence <- if (length(appendix)) paste0(label,"Appendix",appendix[[1]]$latin) else label
    old_id <- attr(bookmark, "w:id")
    xml_remove(xml_find_all(doc, paste0("//w:bookmarkEnd[@w:id='", old_id, "']"), ns))
    xml_remove(bookmark)
    xml_remove(xml_find_all(caption, "./*[not(self::w:pPr)]", ns))
    xml_add_child(caption, node(run(paste0(label, " "))))
    xml_add_child(caption, node(paste0('<w:bookmarkStart w:id="', next_id, '" w:name="', normalized, '"/>')))
    if (nzchar(appendix_prefix)) xml_add_child(caption,node(run(appendix_prefix)))
    for (part in xml_children(read_xml(paste0('<root xmlns:w="', W, '">', field(paste0("SEQ ", sequence, " \\* ARABIC")), '</root>')))) xml_add_child(caption, part)
    xml_add_child(caption, node(paste0('<w:bookmarkEnd w:id="', next_id, '"/>')))
    xml_add_child(caption, node(run(paste0(" — ", caption_text))))
    next_id <- next_id + 1
  }
  # A numbered equation stays editable; attach its field to the same paragraph.
  # Keep a figure, its introductory paragraph and caption on the same page.
  for (picture in xml_find_all(doc,"//w:p[w:r/w:drawing]",ns)) {
    previous <- xml_find_first(picture,"preceding-sibling::w:p[1]",ns)
    group <- list(picture)
    if (!inherits(previous,"xml_missing") && nzchar(text(previous))) group <- c(list(previous),group)
    for (p in group) {
      pr <- xml_find_first(p,"./w:pPr",ns)
      if (inherits(pr,"xml_missing")) pr <- xml_add_child(p,node("<w:pPr/>"),.where=0)
      if (!length(xml_find_all(pr,"./w:keepNext",ns))) xml_add_child(pr,node("<w:keepNext/>"))
    }
  }
  for (p in xml_find_all(doc,"//w:p[starts-with(string(.),'Таблица ')]",ns)) {
    pr <- xml_find_first(p,"./w:pPr",ns)
    if (inherits(pr,"xml_missing")) pr <- xml_add_child(p,node("<w:pPr/>"),.where=0)
    if (!length(xml_find_all(pr,"./w:keepNext",ns))) xml_add_child(pr,node("<w:keepNext/>"))
  }
  numbers <- xml_find_all(doc, "//w:p[w:r/w:instrText[contains(.,'SEQ Формула')]]", ns)
  section <- xml_find_first(doc, "//w:sectPr[last()]", ns)
  page_size <- xml_find_first(section, "./w:pgSz", ns)
  margins <- xml_find_first(section, "./w:pgMar", ns)
  equation_width <- as.integer(attr(page_size, "w:w")) -
    as.integer(attr(margins, "w:left")) - as.integer(attr(margins, "w:right"))
  if (length(numbers) && (is.na(equation_width) || equation_width <= 0)) stop("Cannot determine equation text width")
  for (number in numbers) {
    previous <- xml_find_first(number, "preceding-sibling::w:p[1]", ns)
    if (inherits(previous, "xml_missing") || !length(xml_find_all(previous, ".//m:oMath", ns))) stop("Equation number has no preceding OMML")
    pr <- xml_find_first(previous, "./w:pPr", ns)
    if (inherits(pr, "xml_missing")) pr <- xml_add_child(previous, node("<w:pPr/>"), .where = 0)
    # Standard QMD is retained; the existing Word adapter supplies native
    # numbering and places it against the actual reference's right margin.
    xml_remove(xml_find_all(pr, "./w:pStyle|./w:tabs", ns))
    xml_add_child(pr, node('<w:pStyle w:val="Formula"/>'), .where = 0)
    # Keep only an actual explanation with the equation. An unconditional
    # keepNext would also attach a following figure and displace the equation.
    explanations <- xml_find_first(number, "following-sibling::w:p[1]/w:pPr/w:pStyle[@w:val='FormulaDefinitions']", ns)
    if (!inherits(explanations, "xml_missing")) xml_add_child(pr, node("<w:keepNext/>"))
    xml_add_child(pr, node(paste0('<w:tabs><w:tab w:val="right" w:pos="', equation_width, '"/></w:tabs>')))
    xml_add_child(previous, node('<w:r><w:tab/></w:r>'))
    for (part in xml_find_all(number, "./*[not(self::w:pPr)]", ns)) xml_add_child(previous, part)
    xml_remove(number)
  }
  # Structural preflight: no missing REF targets or duplicate semantic bookmarks.
  names <- vapply(xml_find_all(doc,"//w:bookmarkStart",ns),attr,"",name="w:name")
  refs <- xml_text(xml_find_all(doc,"//w:instrText[starts-with(normalize-space(.),'REF ')]",ns))
  targets <- sub("^\\s*REF\\s+(\\S+).*", "\\1", refs)
  if (anyDuplicated(names) || any(!targets %in% names)) stop("Duplicate bookmark or missing native REF target")
  write_xml(doc,file.path(folder,"word/document.xml"))
  packaged <- tempfile(fileext=".docx")
  zip::zipr(packaged,files=list.files(folder,recursive=TRUE,all.files=TRUE,no..=TRUE),root=folder,mode="mirror",include_directories=FALSE)
  if (!file.copy(packaged,output,overwrite=TRUE)) stop("Cannot replace generated DOCX")
  file.copy(output,file.path(".quarto",paste0(basename(output),".before-word.docx")),overwrite=TRUE)
  if (Sys.getenv("ACADEMIC_SKIP_WORD") != "1") {
    if (.Platform$OS.type != "windows") stop("Final DOCX currently requires the Windows C# service and desktop Word")
    service <- Sys.getenv("ACADEMIC_DOCX_SERVICE")
    if (!nzchar(service)) service <- Sys.which("academic-docx")
    if (!nzchar(service) || !file.exists(service))
      stop("Academic DOCX service is missing: add the published application to PATH or set ACADEMIC_DOCX_SERVICE to its EXE")
    version <- system2(normalizePath(service), "--version", stdout = TRUE, stderr = TRUE)
    if (!is.null(base::attr(version, "status")) || length(version) != 1L ||
        !grepl("^0[.]3[.][0-9]+$", version) || numeric_version(version) < numeric_version("0.3.1"))
      stop("Incompatible Academic DOCX service: this extension requires version >=0.3.1 within 0.3.x; select it through PATH or ACADEMIC_DOCX_SERVICE")
    report <- normalizePath(file.path(".quarto",paste0(basename(output),".finalize.json")), mustWork=FALSE)
    layout_path <- normalizePath(file.path('.quarto', 'academic-layout.json'), mustWork=TRUE)
    layout <- jsonlite::fromJSON(layout_path)
    arguments <- c("finalize-word", shQuote(output), "--report", shQuote(report), "--layout-metadata", shQuote(layout_path))
    if (identical(layout$Standard, 'susu-04-2008')) {
      templates <- normalizePath(file.path(academic_candidates[[1]], 'page-templates', 'sto04'), mustWork=TRUE)
      arguments <- c(arguments, '--page-templates', shQuote(templates))
    }
    status <- system2(normalizePath(service), arguments)
    if (status != 0) stop("C# Word finalization failed; see ",report)
    message("Academic DOCX finalized: ",basename(output))
  } else {
    warning("Diagnostic draft only: fields are not updated; the pre-Word package has known schema errors. Do not treat this file as a final document.")
    message("Academic diagnostic draft: ",basename(output))
  }
}
