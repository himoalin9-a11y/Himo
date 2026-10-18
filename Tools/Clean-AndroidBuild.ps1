$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath '.\Himo.csproj')) {
    throw 'Run this script from the Himo project root (the folder containing Himo.csproj).'
}

$vs = Get-Process -Name devenv -ErrorAction SilentlyContinue
if ($vs) {
    throw 'Close Visual Studio completely before running this script, then run it again.'
}

foreach ($path in @('.\bin', '.\obj')) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "Removed $path" -ForegroundColor DarkYellow
    }
}
Write-Host 'Android build cache cleaned. Next run dotnet restore and build Himo.csproj alone with -m:1.' -ForegroundColor Green
