@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PUBLISH=%~dp0bin\Release\publish-win-x64"
set "INSTALL=C:\HimoServer"
set "SERVICE=HimoApi"

if not exist "%PUBLISH%\Himo.Api.exe" (
  echo ERROR: Publish output not found:
  echo %PUBLISH%\Himo.Api.exe
  echo Run PUBLISH-SERVER-WIN64.bat first.
  pause
  exit /b 1
)

net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo ERROR: Run this file as Administrator.
  pause
  exit /b 1
)

echo === Installing Himo API LAN Server ===
if not exist "%INSTALL%" mkdir "%INSTALL%"
xcopy "%PUBLISH%\*" "%INSTALL%\" /E /I /Y >nul

sc stop "%SERVICE%" >nul 2>&1
sc delete "%SERVICE%" >nul 2>&1
sc create "%SERVICE%" binPath= "\"%INSTALL%\Himo.Api.exe\"" start= auto DisplayName= "Himo API Server" >nul
sc description "%SERVICE%" "Himo messaging API and SignalR LAN server" >nul

netsh advfirewall firewall delete rule name="Himo API 5080" >nul 2>&1
netsh advfirewall firewall add rule name="Himo API 5080" dir=in action=allow protocol=TCP localport=5080 profile=private >nul

sc start "%SERVICE%"
if not "%errorlevel%"=="0" goto :fail

echo.
echo Himo API service installed and started.
echo LAN endpoint: http://SERVER-IP:5080/
echo Health:       http://SERVER-IP:5080/health
echo.
echo Find the server IP with: ipconfig
pause
exit /b 0

:fail
echo.
echo Service installation/start failed. Check Windows Services for HimoApi.
pause
exit /b 1
