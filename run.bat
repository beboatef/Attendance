@echo off
rem ===========================================================================
rem  HrPayroll.Web - launcher for Windows (CMD or PowerShell)
rem
rem  Works both from the repository root and from the published folder, so the
rem  same file is reused as publish\run.bat after publishing.
rem
rem  No Visual Studio, no .NET SDK and no locally installed SQL Server needed.
rem
rem  The database password is NOT stored in this file. It is read from:
rem    - appsettings.Production.json next to the executable, or
rem    - the environment variable ConnectionStrings__DefaultConnection,
rem      which always takes precedence over the JSON files.
rem
rem  Optional overrides set before calling this file:
rem    set ASPNETCORE_URLS=http://localhost:5084
rem    set ASPNETCORE_ENVIRONMENT=Development
rem ===========================================================================

setlocal EnableExtensions
set "SELFDIR=%~dp0"
cd /d "%SELFDIR%"

if not defined ASPNETCORE_ENVIRONMENT set "ASPNETCORE_ENVIRONMENT=Production"
if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://0.0.0.0:5084"

rem --- locate the published executable: same folder first, then .\publish ---
set "APP=%SELFDIR%HrPayroll.Web.exe"
if not exist "%APP%" set "APP=%SELFDIR%publish\HrPayroll.Web.exe"
if not exist "%APP%" goto :nocode
for %%I in ("%APP%") do set "APPDIR=%%~dpI"

echo ============================================================
echo  HrPayroll.Web
echo ============================================================
echo  Executable   : %APP%
echo  Environment  : %ASPNETCORE_ENVIRONMENT%
echo  Listening on : %ASPNETCORE_URLS%
if defined ConnectionStrings__DefaultConnection goto :db_env
if exist "%APPDIR%appsettings.Production.json" goto :db_file
echo  Database     : WARNING - appsettings.Production.json was not found and
echo                 ConnectionStrings__DefaultConnection is not set.
echo                 The app will start but cannot reach SQL Server.
goto :start
:db_env
echo  Database     : connection string from the environment (password hidden)
goto :start
:db_file
echo  Database     : connection string from appsettings.Production.json
goto :start

:start
echo ------------------------------------------------------------
echo  Press CTRL+C in this window to stop the application.
echo ------------------------------------------------------------
echo.

"%APP%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" goto :failed
echo  HrPayroll.Web stopped with exit code 0.
exit /b 0

:failed
echo  [ERROR] HrPayroll.Web exited with code %EXITCODE%.
echo  [ERROR] Review the messages above for the cause.
echo.
echo  Press any key to close this window.
pause >nul
exit /b %EXITCODE%

:nocode
echo  [ERROR] HrPayroll.Web.exe was not found.
echo.
echo  Looked for these files:
echo    %SELFDIR%HrPayroll.Web.exe
echo    %SELFDIR%publish\HrPayroll.Web.exe
echo.
echo  Build the deployment folder first, from the repository root:
echo    publish.bat
echo.
pause
exit /b 1
