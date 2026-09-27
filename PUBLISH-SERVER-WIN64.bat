@echo off
setlocal
cd /d "%~dp0Himo.Api"
call PUBLISH-SERVER-WIN64.bat
exit /b %errorlevel%
