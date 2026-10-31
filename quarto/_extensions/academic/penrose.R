# Standard project pre-render hook. Source files and .trio.json use Penrose's
# own formats. Generated SVGs follow the same relative tree under .quarto/.
source_root <- "figures/penrose"
trios <- list.files(source_root, pattern = "[.]trio[.]json$", recursive = TRUE, full.names = TRUE)
if (length(trios)) {
  node <- Sys.getenv("ANK_PENROSE_NODE")
  cli <- Sys.getenv("ANK_PENROSE_CLI")
  converter <- Sys.getenv("ANK_SVG_CONVERTER", unset = Sys.which("rsvg-convert"))
  if (!nzchar(node) || !file.exists(node) || !nzchar(cli) || !file.exists(cli))
    stop("Penrose is missing: run ANK-DOCX.cmd setup and build through ANK, or use its local R installation")
  if (!nzchar(converter) || !file.exists(converter)) stop("SVG converter is missing: run ANK-DOCX.cmd setup")
  render_penrose <- function(trio) {
    relative <- substring(trio, nchar(source_root) + 2L)
    output <- file.path(".quarto", "penrose", sub("[.]trio[.]json$", ".svg", relative))
    dir.create(dirname(output), recursive = TRUE, showWarnings = FALSE)
    temporary <- tempfile("penrose-", tmpdir = dirname(output), fileext = ".svg")
    compatible <- paste0(temporary, ".word.svg")
    on.exit(unlink(c(temporary, compatible)), add = TRUE)
    trio <- normalizePath(trio, winslash = "/", mustWork = TRUE)
    log <- system2(node, c(shQuote(cli), "trio", "--trio", shQuote(trio), "--out", shQuote(temporary)),
                   stdout = TRUE, stderr = TRUE, timeout = 120)
    if (!is.null(attr(log, "status")) || !file.exists(temporary) || file.info(temporary)$size == 0)
      stop("Penrose rendering failed for ", relative, ":\n", paste(log, collapse = "\n"))
    # Word ignores Penrose's CSS font shorthand. Cairo's SVG output outlines
    # the glyphs while keeping the entire diagram vector, without our own parser.
    converted <- system2(converter, c("--format", "svg", "--output", shQuote(compatible), shQuote(temporary)),
                         stdout = TRUE, stderr = TRUE, timeout = 120)
    if (!is.null(attr(converted, "status")) || !file.exists(compatible) || file.info(compatible)$size == 0)
      stop("Penrose SVG normalization failed:\n", paste(converted, collapse = "\n"))
    if (!file.copy(compatible, output, overwrite = TRUE)) stop("Cannot save Penrose SVG: ", output)
    message("Penrose SVG: ", output)
  }
  invisible(lapply(trios, render_penrose))
}
