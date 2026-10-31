@echo off
setlocal
call "%~dp0build-service.cmd" %*
if errorlevel 1 exit /b %ERRORLEVEL%
"%~dp0.tools\academic-docx\academic-docx.exe" package-service "%~dp0." "%~dp0.tools\academic-docx"
exit /b %ERRORLEVEL%
