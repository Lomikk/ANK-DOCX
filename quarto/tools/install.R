# Run once with Rscript --vanilla tools/install.R in this project.
source(".Rprofile")
renv::restore(project=getwd(), prompt=FALSE)
