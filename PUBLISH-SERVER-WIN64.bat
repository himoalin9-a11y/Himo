@echo off
setlocal
cd /d "%~dp0"
echo === Himo.Api LAN Server - Windows x64 Publish ===
dotnet restore Himo.Api.csproj || goto :fail
dotnet publish Himo.Api.csproj -c Release -r win-x64 --self-contained true -o bin\Release\publish-win-x64 || goto :fail
echo.
echo Publish completed.
echo Output: %CD%\bin\Release\publish-win-x64
echo.
echo Copy this folder to the server PC, then run INSTALL-HIMO-SERVER.bat as Administrator.
pause
exit /b 0
:fail
echo.
echo Publish failed.
pause
exit /b 1
