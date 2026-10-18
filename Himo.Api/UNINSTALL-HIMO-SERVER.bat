@echo off
setlocal
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo ERROR: Run this file as Administrator.
  pause
  exit /b 1
)
sc stop HimoApi >nul 2>&1
sc delete HimoApi >nul 2>&1
netsh advfirewall firewall delete rule name="Himo API 5080" >nul 2>&1
echo HimoApi service and firewall rule removed.
pause
