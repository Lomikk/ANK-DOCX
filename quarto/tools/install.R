# Run once with Rscript --vanilla tools/install.R in this project.
source("renv/activate.R")
renv::restore(project=getwd(), prompt=FALSE)
