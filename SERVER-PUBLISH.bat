@echo off
setlocal
cd /d "%~dp0Himo.Api"
call SERVER-PUBLISH.bat
exit /b %errorlevel%
