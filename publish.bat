@echo off
rem ===========================================================================
rem  Builds the Windows deployment folder for HrPayroll.Web.
rem
rem  Requires the .NET SDK once (see requirements below). Produces .\publish\
rem  containing the self-contained application plus run.bat, so the target
rem  machine does not need a separately installed .NET runtime.
rem
rem  The database password is never written by this script: configuration
rem  stays in src\HrPayroll.Web\appsettings.Production.json, which is excluded
rem  from source control and copied into the publish output by the .NET SDK.
rem ===========================================================================

setlocal EnableExtensions
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 goto :nodotnet

echo  Configuration : Release
echo  Runtime       : win-x64 (self-contained)
echo  Output folder : %CD%\publish
echo.

dotnet publish "src\HrPayroll.Web\HrPayroll.Web.csproj" -c Release -r win-x64 --self-contained true -o "publish" -p:PublishReadyToRun=false
if errorlevel 1 goto :failed

if not exist "publish\HrPayroll.Web.exe" goto :failed
if not exist "publish\run.bat" goto :failed

echo.
echo  [OK] Deployment folder: %CD%\publish
echo  [OK] Start the application with:  publish\run.bat
exit /b 0

:nodotnet
echo  [ERROR] The .NET SDK was not found in PATH.
echo.
echo  Install the .NET 9 SDK (or newer) from:
echo    https://dotnet.microsoft.com/download/dotnet/9.0
echo.
echo  Then open a NEW command prompt and run publish.bat again.
echo.
pause
exit /b 1

:failed
echo.
echo  [ERROR] Publishing failed, or the expected output files are missing.
echo  [ERROR] Review the messages above.
echo.
pause
exit /b 1
