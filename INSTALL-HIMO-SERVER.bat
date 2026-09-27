@echo off
setlocal
cd /d "%~dp0Himo.Api"
call INSTALL-HIMO-SERVER.bat
exit /b %errorlevel%
