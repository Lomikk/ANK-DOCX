# Project entry point only. The implementation belongs to the installed extension.
# Quarto adds the GitHub owner namespace on remote installation; local sources
# have no namespace. Reject ambiguity rather than choosing an arbitrary copy.
academic_candidates <- c(Sys.glob("_extensions/academic"),
                         Sys.glob("_extensions/*/academic"))
academic_candidates <- academic_candidates[
  file.exists(file.path(academic_candidates, "_extension.yml"))]
if (length(academic_candidates) != 1L) {
  stop("Expected one installed academic extension; found ",
       length(academic_candidates), ". Install it with quarto add <ANK source>.")
}
academic_script <- "setup.R"
if (sys.nframe() == 0L) {
  args <- commandArgs(trailingOnly = TRUE)
  if (identical(args, "finalize")) academic_script <- "finalize.R"
  else if (identical(args, "diagrams")) academic_script <- "penrose.R"
  else stop("Usage: Rscript tools/academic.R diagrams|finalize")
}
source(file.path(academic_candidates[[1]], academic_script),
       local = .GlobalEnv, encoding = "UTF-8")
