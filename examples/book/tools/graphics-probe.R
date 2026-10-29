# Render the actual model plot through two supported devices, without a second model.
args <- commandArgs(trailingOnly = TRUE)
output <- if (length(args)) args[[1]] else file.path("..", "..", "build", "graphics-quality")
dir.create(output, recursive = TRUE, showWarnings = FALSE)
code <- tempfile(fileext = ".R")
invisible(knitr::purl("chapters/model.qmd", output = code, quiet = TRUE))
model <- new.env()
# The extracted chapter also draws base persp() plots. Use a device that
# supports its fonts instead of Rscript's automatic PostScript/PDF device.
invisible(svglite::svgstring(bg = "transparent"))
invisible(tryCatch(sys.source(code, envir = model), finally = grDevices::dev.off()))
unlink(code)
ggplot2::ggsave(file.path(output, "model-600dpi.png"), model$model_plot,
               device = ragg::agg_png, width = 6.2, height = 3, units = "in", dpi = 600, bg = "transparent")
ggplot2::ggsave(file.path(output, "model-vector.svg"), model$model_plot,
               device = svglite::svglite, width = 6.2, height = 3, units = "in", bg = "transparent")
