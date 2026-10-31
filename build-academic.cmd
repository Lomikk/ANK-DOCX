@echo off
setlocal
set "TASK_ROOT=%~dp0"
set "RENV_PATHS_ROOT=%TASK_ROOT%.tools\renv"
set "RENV_PATHS_CACHE=%TASK_ROOT%.tools\renv\cache"
set "RENV_PATHS_SOURCE=%TASK_ROOT%.tools\renv\source"
set "RENV_PATHS_BINARY=%TASK_ROOT%.tools\renv\binary"
set "RENV_PATHS_SANDBOX=%TASK_ROOT%.tools\renv\sandbox"
if not defined ACADEMIC_DOCX_SERVICE set "ACADEMIC_DOCX_SERVICE=%TASK_ROOT%.tools\academic-docx\academic-docx.exe"
if not exist "%ACADEMIC_DOCX_SERVICE%" (
  echo Academic DOCX service is missing. Run setup-academic.cmd. 1>&2
  exit /b 1
)
"%ACADEMIC_DOCX_SERVICE%" quarto-path "%TASK_ROOT%." > "%TASK_ROOT%.tools\quarto-path.txt"
if errorlevel 1 exit /b %ERRORLEVEL%
set /p TASK_QUARTO=<"%TASK_ROOT%.tools\quarto-path.txt"
for %%Q in ("%TASK_QUARTO%") do set "PATH=%%~dpQ;%PATH%"
if exist "%TASK_ROOT%.tools\R\bin\Rscript.exe" set "PATH=%TASK_ROOT%.tools\R\bin;%PATH%"
if exist "%TASK_ROOT%.tools\R\bin\Rscript.exe" set "QUARTO_R=%TASK_ROOT%.tools\R\bin\Rscript.exe"
if exist "%TASK_ROOT%.tools\rsvg-convert-static-1.0.0\rsvg-convert.exe" set "PATH=%TASK_ROOT%.tools\rsvg-convert-static-1.0.0;%PATH%"
set "LC_ALL="
set "LC_CTYPE="
set "LC_COLLATE="
set "LC_MONETARY="
set "LC_TIME="
rem An explicit project/file is forwarded unchanged to Quarto. Without one,
rem use the maintained integration example; existing --profile usage stays valid.
set "TASK_FIRST=%~1"
if not defined TASK_FIRST goto render_example
if "%TASK_FIRST:~0,1%"=="-" goto render_example
quarto render %* --to academic-docx --no-clean
exit /b %ERRORLEVEL%
:render_example
pushd "%TASK_ROOT%examples\book"
quarto render --to academic-docx --no-clean %*
set "TASK_RESULT=%ERRORLEVEL%"
popd
exit /b %TASK_RESULT%
