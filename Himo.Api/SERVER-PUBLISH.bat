@echo off
setlocal
cd /d "%~dp0"
echo === Himo.Api Release Publish ===
dotnet restore Himo.Api.csproj || goto :fail
dotnet publish Himo.Api.csproj -c Release -o bin\Release\publish || goto :fail
echo.
echo Publish completed. Output:
echo %CD%\bin\Release\publish
echo.
pause
exit /b 0
:fail
echo.
echo Publish failed.
pause
exit /b 1
