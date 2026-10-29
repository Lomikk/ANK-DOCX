required <- c("flextable", "equatags", "officer", "yaml", "xml2", "zip", "jsonlite", "ggplot2", "ggpattern", "ragg", "svglite")
missing <- required[!vapply(required, requireNamespace, logical(1), quietly = TRUE)]
if (length(missing)) stop("Install the declared R dependencies: ", paste(missing, collapse = ", "))
flextable::set_flextable_defaults(font.family = "Times New Roman", font.size = 11, padding = 3, decimal.mark = ",", big.mark = " ")


# Common, print-friendly plotting defaults for the academic Quarto MVP.
# These are project conventions, not a claim of full STO compliance.
# Source setup.R once in every independently evaluated chapter.

ank_graph_font <- "Times New Roman"
ank_line_types <- c("solid", "dashed", "dotted", "dotdash", "longdash", "twodash")
ank_point_shapes <- c(16, 1, 17, 15, 3, 4)
# Available pattern names for ggpattern::geom_*_pattern().
ank_fill_patterns <- c("stripe", "crosshatch", "circle", "none")
ank_decimal_labels <- scales::label_number(decimal.mark = ",")

# Apply this after constructing the header, body, and footer. Early flextable
# themes do not style rows that are appended afterwards.
ank_style_table <- function(ft, layout = "autofit", width = 1) {
  ft <- flextable::theme_box(ft)
  ft <- flextable::font(ft, fontname = "Times New Roman", part = "all")
  ft <- flextable::fontsize(ft, size = 11, part = "all")
  ft <- flextable::align(ft, align = "center", part = "header")
  ft <- flextable::valign(ft, valign = "center", part = "all")
  # Word interprets width as a fraction of the available text area. Explicit
  # column widths apply only when the author selects layout = "fixed".
  ft <- flextable::set_table_properties(ft, layout = layout, width = width,
                                        opts_word = list(split = FALSE, repeat_headers = TRUE))
  flextable::paginate(ft, init = FALSE, hdr_ftr = TRUE)
}

ank_theme_2d <- function(base_size = 11, base_family = ank_graph_font) {
  style <- ggplot2::theme_bw(base_size = base_size, base_family = base_family) +
    ggplot2::theme(
      panel.background = ggplot2::element_rect(fill = "transparent", colour = NA),
      plot.background = ggplot2::element_rect(fill = "transparent", colour = NA),
      panel.border = ggplot2::element_rect(fill = NA, colour = "black", linewidth = 0.4),
      panel.grid.major = ggplot2::element_line(colour = "grey85", linewidth = 0.2),
      panel.grid.minor = ggplot2::element_blank(),
      axis.line = ggplot2::element_blank(),
      axis.ticks = ggplot2::element_line(colour = "black", linewidth = 0.3),
      axis.text = ggplot2::element_text(colour = "black", size = base_size - 1),
      axis.title = ggplot2::element_text(colour = "black", size = base_size),
      legend.position = "bottom",
      legend.justification = "center",
      legend.background = ggplot2::element_rect(fill = "transparent", colour = NA),
      legend.key = ggplot2::element_rect(fill = "transparent", colour = NA),
      legend.key.width = grid::unit(1.1, "cm"),
      legend.text = ggplot2::element_text(size = base_size - 1),
      plot.margin = ggplot2::margin(4, 8, 4, 6)
    )
  # ggplot2 >= 4 lets the theme own default palettes as well as typography.
  if (utils::packageVersion("ggplot2") >= "4.0.0") {
    style <- style + ggplot2::theme(
      geom = ggplot2::element_geom(ink = "black", paper = "white", accent = "grey40"),
      palette.colour.discrete = scales::pal_grey(start = 0.12, end = 0.65),
      palette.fill.discrete = scales::pal_grey(start = 0.92, end = 0.45),
      palette.colour.continuous = c("grey70", "black"),
      palette.fill.continuous = c("grey95", "grey20")
    )
  }
  style
}

# Applied automatically to ggplot2 charts after sourcing setup.R.
# Specific graphs may override individual settings with + theme(...).
ggplot2::theme_set(ank_theme_2d())
# Common line/point sizes; scientific series mappings still belong to each chart.
ggplot2::update_geom_defaults("line", list(linewidth = 0.5))
ggplot2::update_geom_defaults("point", list(size = 2.1, stroke = 0.45))
# ggpattern uses its own geom defaults. Spacing controls repetition; density
# controls filled band width as well as pattern_size controlling its outline.
ggplot2::update_geom_defaults(ggpattern::GeomColPattern, list(
  colour = "black", fill = "white", linewidth = 0.25,
  pattern_colour = "black", pattern_fill = "black",
  pattern_size = 0.08, pattern_density = 0.10, pattern_spacing = 0.03
))

# Base graphics has no ggplot2-style theme object, so 3D needs a wrapper.
# z[i, j] corresponds to the point (x[i], y[j]).
# Default is a clear wireframe; style="shaded" adds greyscale relief.
# Arguments in ... override the style defaults (e.g. theta, xlab, col).
ank_persp_3d <- function(x, y, z, style = c("wireframe", "shaded"), ...) {
  style <- match.arg(style)
  dots <- list(...)
  if (length(dots) && (is.null(names(dots)) || any(!nzchar(names(dots))))) {
    stop("Pass additional persp() arguments by name")
  }

  wireframe <- identical(style, "wireframe")
  defaults <- list(
    x = x, y = y, z = z,
    theta = -47, phi = 27, d = 120, scale = TRUE, expand = 0.78,
    col = if (wireframe) "white" else "grey80",
    border = if (wireframe) "black" else "grey45",
    lwd = if (wireframe) 0.35 else 0.2,
    shade = if (wireframe) NA_real_ else 0.6,
    ltheta = 120, lphi = 30,
    box = TRUE, axes = TRUE, ticktype = "detailed", nticks = 3,
    xlab = "x", ylab = "y", zlab = "z",
    family = ank_graph_font, bg = "transparent", fg = "black",
    col.axis = "black", col.lab = "black",
    cex.axis = 0.8, cex.lab = 0.85,
    mar = c(1, 1, 1, 1)
  )
  args <- utils::modifyList(defaults, dots)
  # par(mar=...) must be applied to the device, not passed to persp(...).
  plot_mar <- args$mar
  args$mar <- NULL
  old_mar <- graphics::par(mar = plot_mar)
  on.exit(graphics::par(mar = old_mar), add = TRUE)
  labels <- list(x = args$xlab, y = args$ylab, z = args$zlab)
  # Native persp() labels overlap detailed tick labels. Draw them separately,
  # but retain persp()'s own ticks, box, and projection matrix.
  args[c("xlab", "ylab", "zlab")] <- list("", "", "")
  projection <- do.call(graphics::persp, args)
  limits <- list(
    x = if (is.null(args$xlim)) range(x) else args$xlim,
    y = if (is.null(args$ylim)) range(y) else args$ylim,
    z = if (is.null(args$zlim)) range(z, na.rm = TRUE) else args$zlim
  )
  project <- function(x, y, z) grDevices::trans3d(x, y, z, projection)
  bottom <- limits$z[[1]]
  x_edges <- lapply(limits$y, function(y0) project(mean(limits$x), y0, bottom))
  y_edges <- lapply(limits$x, function(x0) project(x0, mean(limits$y), bottom))
  x_pos <- x_edges[[which.min(vapply(x_edges, `[[`, numeric(1), "y"))]]
  y_pos <- y_edges[[which.min(vapply(y_edges, `[[`, numeric(1), "y"))]]
  corners <- expand.grid(x = limits$x, y = limits$y)
  z_edges <- lapply(seq_len(nrow(corners)), function(i) {
    project(corners$x[[i]], corners$y[[i]], mean(limits$z))
  })
  z_pos <- z_edges[[which.min(vapply(z_edges, `[[`, numeric(1), "x"))]]
  usr <- graphics::par("usr")
  dx <- diff(usr[1:2]); dy <- diff(usr[3:4])
  draw_label <- function(pos, label, dx = 0, dy = 0) {
    if (!is.null(label) && length(label) && nzchar(as.character(label)[[1]])) {
      graphics::text(pos$x + dx, pos$y + dy, labels = label,
                     cex = args$cex.lab, family = args$family,
                     col = args$col.lab, xpd = NA)
    }
  }
  draw_label(x_pos, labels$x, dx = 0.10 * dx, dy = -0.14 * dy)
  draw_label(y_pos, labels$y, dx = -0.10 * dx, dy = -0.14 * dy)
  draw_label(z_pos, labels$z, dx = -0.17 * dx)
  invisible(projection)
}
