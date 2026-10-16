@echo off
setlocal
cd /d "%~dp0"
echo ================================================
echo Himo - Android build reset
 echo This removes only generated build folders: bin, obj, .vs
 echo Close Visual Studio before continuing.
echo ================================================
pause
powershell -NoProfile -ExecutionPolicy Bypass -Command "$root=(Get-Location).Path; $targets=Get-ChildItem -LiteralPath $root -Directory -Force -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('bin','obj','.vs') } | Sort-Object FullName -Descending; foreach($d in $targets){ for($i=1;$i -le 8;$i++){ try{ if(Test-Path -LiteralPath $d.FullName){ Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction Stop }; break } catch { if($i -eq 8){ Write-Error ('Could not remove: '+$d.FullName+' :: '+$_.Exception.Message); exit 1 }; Start-Sleep -Milliseconds (500*$i) } } }; Write-Host 'Generated build folders removed successfully.'"
if errorlevel 1 (
 echo.
 echo Build reset failed. Make sure Visual Studio and Android build tools are closed.
 pause
 exit /b 1
)
echo.
echo Reset complete. Now open Himo.sln in Visual Studio and choose Rebuild All.
pause
