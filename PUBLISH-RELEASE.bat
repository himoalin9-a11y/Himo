@echo off
setlocal
cd /d "%~dp0"

echo ==========================================
echo Himo - Release APK / AAB Publisher
echo ==========================================
echo.
echo 1. Restoring and building Release...
dotnet restore Himo.csproj
if errorlevel 1 goto :fail

dotnet publish Himo.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=apk
if errorlevel 1 goto :fail

echo.
echo Release publish completed.
echo Check:
echo   bin\Release\net10.0-android\publish\
echo for the generated Android package files.
echo.
echo APK format is forced for this stage.
echo The generated APK will be under the publish folder.
echo.
pause
exit /b 0

:fail
echo.
echo Release publish FAILED. Review the error output above.
pause
exit /b 1
