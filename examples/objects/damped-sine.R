# Run from repository root: Rscript examples/objects/damped-sine.R [output-root]
# Pure R: ggplot2 + plotmath, svglite and ragg. No TeX compilation.
args <- commandArgs(trailingOnly = TRUE)
output_root <- if (length(args)) args[[1]] else "output"
output <- file.path(output_root, "graphics")
pdf_output <- file.path(output_root, "pdf")
dir.create(output, recursive = TRUE, showWarnings = FALSE)
dir.create(pdf_output, recursive = TRUE, showWarnings = FALSE)

# Parameters are shared by the calculation and the annotation.
parameters <- list(A = 1, tau = 3, nu = 1)
stopifnot(parameters$A > 0, parameters$tau > 0, parameters$nu > 0)
time <- seq(0, 8, length.out = 1601)

# One ordinary R expression is both evaluated and displayed by plotmath.
model <- quote(A * e^{-t/tau} * sin(2 * pi * nu * t))
context <- c(parameters, list(t = time, e = exp(1)))
signal <- eval(model, envir = context, enclos = baseenv())
envelope <- parameters$A * exp(-time / parameters$tau)
stopifnot(all(is.finite(signal)), all(abs(signal) <= envelope + 1e-12))

data <- data.frame(t = time, signal = signal, envelope = envelope)
bounds <- data.frame(t = rep(time, 2), y = c(envelope, -envelope),
                     side = rep(c("upper", "lower"), each = length(time)))
formula <- as.expression(substitute(italic(y)(italic(t)) == rhs, list(rhs = model)))
parameter_label <- bquote(paste(A == .(parameters$A), ",   ",
                                tau == .(parameters$tau), " с,   ",
                                nu == .(parameters$nu), " Гц"))

colours <- c("Колебание" = "#205F8B", "Огибающие" = "#7B838A")
line_types <- c("Колебание" = "solid", "Огибающие" = "dashed")
plot <- ggplot2::ggplot() +
  ggplot2::geom_line(data = data,
                    ggplot2::aes(t, signal, colour = "Колебание", linetype = "Колебание"),
                    linewidth = 0.6, lineend = "round") +
  ggplot2::geom_line(data = bounds,
                    ggplot2::aes(t, y, group = side, colour = "Огибающие", linetype = "Огибающие"),
                    linewidth = 0.35) +
  ggplot2::scale_colour_manual(values = colours, breaks = names(colours), name = NULL) +
  ggplot2::scale_linetype_manual(values = line_types, breaks = names(colours), name = NULL) +
  ggplot2::scale_x_continuous(breaks = 0:8, expand = ggplot2::expansion(mult = c(0, 0))) +
  ggplot2::scale_y_continuous(breaks = pretty(c(-parameters$A, parameters$A), n = 5),
                            labels = scales::label_number(decimal.mark = ",")) +
  ggplot2::coord_cartesian(ylim = c(-1.18, 1.18) * parameters$A, expand = FALSE) +
  ggplot2::labs(
    title = formula,
    subtitle = parameter_label,
    x = expression(paste("Время ", italic(t), ", с")),
    y = expression(paste("Амплитуда ", italic(y)))
  ) +
  ggplot2::theme_bw(base_family = "Times New Roman", base_size = 12) +
  ggplot2::theme(
    plot.title.position = "plot",
    plot.title = ggplot2::element_text(hjust = 0.5, size = 18,
                                     margin = ggplot2::margin(b = 7)),
    plot.subtitle = ggplot2::element_text(hjust = 0.5, size = 11,
                                        margin = ggplot2::margin(b = 12)),
    axis.text = ggplot2::element_text(size = 10.5, colour = "#222222"),
    axis.title.x = ggplot2::element_text(margin = ggplot2::margin(t = 8)),
    axis.title.y = ggplot2::element_text(margin = ggplot2::margin(r = 8)),
    axis.ticks = ggplot2::element_line(colour = "grey55", linewidth = 0.25),
    panel.border = ggplot2::element_rect(colour = "grey55", linewidth = 0.3),
    panel.grid.major = ggplot2::element_line(colour = "grey90", linewidth = 0.2),
    panel.grid.minor = ggplot2::element_blank(),
    legend.position = "inside",
    legend.position.inside = c(0.98, 0.97),
    legend.justification = c(1, 1),
    legend.background = ggplot2::element_rect(fill = "white", colour = NA),
    legend.key.width = grid::unit(1.05, "cm"),
    legend.text = ggplot2::element_text(size = 10),
    legend.margin = ggplot2::margin(3, 5, 3, 5),
    plot.margin = ggplot2::margin(10, 13, 10, 8)
  )

name <- file.path(output, "ggplot2-damped-sine")
ggplot2::ggsave(paste0(name, ".svg"), plot, device = svglite::svglite,
               width = 6.8, height = 3.9, units = "in", bg = "white")
ggplot2::ggsave(paste0(name, ".png"), plot, device = ragg::agg_png,
               width = 6.8, height = 3.9, units = "in", dpi = 600, bg = "white")
ggplot2::ggsave(file.path(pdf_output, "ggplot2-damped-sine.pdf"), plot, device = grDevices::cairo_pdf,
               width = 6.8, height = 3.9, units = "in", bg = "white")
utils::write.csv(data, paste0(name, ".csv"), row.names = FALSE, fileEncoding = "UTF-8")
message("Saved SVG/PNG: ", name, "; PDF directory: ", pdf_output)
