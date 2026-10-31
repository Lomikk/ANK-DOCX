@echo off
rem Compile the self-contained C# service; requires SDK and PIA. See tools/README.md.
setlocal
set "TASK_ROOT=%~dp0..\"
dotnet publish "%TASK_ROOT%tools\AcademicDocx\AcademicDocx.csproj" -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o "%TASK_ROOT%.tools\academic-docx" --nologo %*
exit /b %ERRORLEVEL%
