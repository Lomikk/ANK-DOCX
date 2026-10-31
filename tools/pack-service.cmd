@echo off
rem Build the release ZIP and update its SHA-256 manifest. See tools/README.md.
setlocal
set "TASK_ROOT=%~dp0..\"
call "%~dp0build-service.cmd" %*
if errorlevel 1 exit /b %ERRORLEVEL%
"%TASK_ROOT%.tools\academic-docx\academic-docx.exe" package-service "%TASK_ROOT%." "%TASK_ROOT%.tools\academic-docx"
exit /b %ERRORLEVEL%
