@echo off
setlocal
cd /d "%~dp0"
set "colorCsc=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%colorCsc%" exit /b 2
"%colorCsc%" /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll /out:"%~dp0DisplayColorGuard.new.exe" "%~dp0DisplayColorGuard.cs"
if errorlevel 1 exit /b 3
if not exist "%~dp0DisplayColorGuard.exe" goto replace
start "" /wait "%~dp0DisplayColorGuard.exe" disable
if errorlevel 1 exit /b 4
:replace
move /y "%~dp0DisplayColorGuard.new.exe" "%~dp0DisplayColorGuard.exe" >nul
if errorlevel 1 exit /b 5
start "" /wait "%~dp0DisplayColorGuard.exe" enable
exit /b %errorlevel%
