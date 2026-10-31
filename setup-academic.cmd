@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "TASK_ROOT=%~dp0"
set "TASK_SERVICE=%TASK_ROOT%.tools\academic-docx\academic-docx.exe"
if /I "%~1"=="--development" goto development
if not "%~1"=="" if /I not "%~1"=="--service-only" (
  echo Usage: setup-academic.cmd [--service-only ^| --development] 1>&2
  exit /b 1
)
call "%TASK_ROOT%tools\service-release.cmd"
if not defined TASK_SERVICE_SHA256 exit /b 1
if not exist "%TASK_ROOT%.tools\downloads" mkdir "%TASK_ROOT%.tools\downloads"
set "TASK_ARCHIVE=%TASK_ROOT%.tools\downloads\academic-docx-%TASK_SERVICE_VERSION%-win-x64.zip"
if not exist "%TASK_SERVICE%" goto install
"%TASK_SERVICE%" --version > "%TASK_ROOT%.tools\downloads\service-version.txt"
if errorlevel 1 goto install
set /p TASK_INSTALLED=<"%TASK_ROOT%.tools\downloads\service-version.txt"
if "%TASK_INSTALLED%"=="%TASK_SERVICE_VERSION%" goto ready
:install
if not exist "%TASK_ARCHIVE%" goto download
call :verify_archive
if not errorlevel 1 goto extract
:download
if defined ANK_DOCX_SERVICE_ARCHIVE (
  for %%A in ("%ANK_DOCX_SERVICE_ARCHIVE%") do copy /y "%%~fA" "%TASK_ARCHIVE%" >nul
  if errorlevel 1 exit /b 1
) else (
  echo Downloading Academic DOCX %TASK_SERVICE_VERSION%...
  curl.exe --fail --location --retry 3 --output "%TASK_ARCHIVE%.partial" "%TASK_SERVICE_URL%"
  if errorlevel 1 exit /b 1
  move /y "%TASK_ARCHIVE%.partial" "%TASK_ARCHIVE%" >nul
  if errorlevel 1 exit /b 1
)
call :verify_archive
if errorlevel 1 (
  echo Academic DOCX archive checksum mismatch. Nothing was installed. 1>&2
  exit /b 1
)
:extract
if not exist "%TASK_ROOT%.tools\academic-docx" mkdir "%TASK_ROOT%.tools\academic-docx"
tar.exe -xf "%TASK_ARCHIVE%" -C "%TASK_ROOT%.tools\academic-docx"
if errorlevel 1 exit /b 1
"%TASK_SERVICE%" --version > "%TASK_ROOT%.tools\downloads\service-version.txt"
if errorlevel 1 exit /b 1
set /p TASK_INSTALLED=<"%TASK_ROOT%.tools\downloads\service-version.txt"
if not "%TASK_INSTALLED%"=="%TASK_SERVICE_VERSION%" exit /b 1
:ready
if /I "%~1"=="--service-only" exit /b 0
"%TASK_SERVICE%" setup "%TASK_ROOT%."
exit /b %ERRORLEVEL%
:development
call "%TASK_ROOT%build-service.cmd"
if errorlevel 1 exit /b %ERRORLEVEL%
"%TASK_SERVICE%" setup "%TASK_ROOT%." --development
exit /b %ERRORLEVEL%
:verify_archive
set "TASK_ACTUAL_SHA="
certutil.exe -hashfile "%TASK_ARCHIVE%" SHA256 > "%TASK_ARCHIVE%.sha256"
if errorlevel 1 exit /b 1
for /f "usebackq skip=1 tokens=*" %%H in ("%TASK_ARCHIVE%.sha256") do if not defined TASK_ACTUAL_SHA set "TASK_ACTUAL_SHA=%%H"
set "TASK_ACTUAL_SHA=%TASK_ACTUAL_SHA: =%"
if /I not "%TASK_ACTUAL_SHA%"=="%TASK_SERVICE_SHA256%" exit /b 1
exit /b 0
