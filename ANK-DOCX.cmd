@echo off
setlocal
if /I "%~1"=="setup" goto setup
if not exist "%~dp0.tools\academic-docx\academic-docx.exe" (
  echo Run ANK-DOCX.cmd setup first. 1>&2
  if "%~1"=="" pause
  exit /b 1
)
"%~dp0.tools\academic-docx\academic-docx.exe" workspace "%~dp0." %*
exit /b %ERRORLEVEL%
:setup
call "%~dp0setup-academic.cmd"
exit /b %ERRORLEVEL%
