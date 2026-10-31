@echo off
setlocal EnableExtensions DisableDelayedExpansion
if /I "%~1"=="setup" goto setup
if exist "%~dp0.tools\academic-docx\academic-docx.exe" goto run
if "%~1"=="" goto first_run
echo Run ANK-DOCX.cmd setup first. 1>&2
exit /b 1
:run
"%~dp0.tools\academic-docx\academic-docx.exe" workspace "%~dp0." %*
exit /b %ERRORLEVEL%
:first_run
for /f "tokens=2 delims=:" %%C in ('chcp') do set "TASK_CODEPAGE=%%C"
chcp 65001 >nul
echo ANK DOCX — первый запуск
echo Зависимости будут установлены в .tools внутри репозитория.
choice /c 10 /n /m "1 — установить и продолжить, 0 — выход: "
if errorlevel 2 goto cancel
call "%~dp0setup-academic.cmd"
set "TASK_SETUP_RESULT=%ERRORLEVEL%"
chcp %TASK_CODEPAGE% >nul
if not "%TASK_SETUP_RESULT%"=="0" (
  pause
  exit /b %TASK_SETUP_RESULT%
)
goto run
:cancel
chcp %TASK_CODEPAGE% >nul
exit /b 0
:setup
call "%~dp0setup-academic.cmd" %2 %3
exit /b %ERRORLEVEL%
