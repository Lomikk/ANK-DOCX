@echo off
setlocal
set "TASK_ROOT=%~dp0"
if not defined ACADEMIC_DOCX_SERVICE set "ACADEMIC_DOCX_SERVICE=%TASK_ROOT%.tools\academic-docx\academic-docx.exe"
if not exist "%ACADEMIC_DOCX_SERVICE%" (
  echo Academic DOCX service is missing. Run setup-academic.cmd. 1>&2
  exit /b 1
)
if exist "%TASK_ROOT%.tools\quarto-1.10.19\bin\quarto.exe" (
  rem Quarto Book embeds its own path in Markdown; Pandoc drops the backslash
  rem before .tools, breaking the built-in title-block template lookup.
  if not exist "%TEMP%\ank-quarto-1.10.19\bin\quarto.exe" (
    if exist "%TEMP%\ank-quarto-1.10.19" (
      echo Quarto alias path is occupied: %TEMP%\ank-quarto-1.10.19 1>&2
      exit /b 1
    )
    mklink /J "%TEMP%\ank-quarto-1.10.19" "%TASK_ROOT%.tools\quarto-1.10.19" >nul
    if errorlevel 1 (
      echo Cannot create Quarto junction in TEMP. 1>&2
      exit /b 1
    )
  )
  set "PATH=%TEMP%\ank-quarto-1.10.19\bin;%PATH%"
)
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
