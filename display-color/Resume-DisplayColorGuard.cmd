@echo off
if not exist "%~dp0DisplayColorGuard.exe" exit /b 2
start "" /wait "%~dp0DisplayColorGuard.exe" resume
exit /b %errorlevel%
