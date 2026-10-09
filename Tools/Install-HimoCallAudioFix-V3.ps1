$ErrorActionPreference = 'Stop'

$packageVersion = '0.9.3.15'
$tfm = 'net10.0-android36.0'
$nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15'
$target = Join-Path $nugetRoot "lib\$tfm\libwebrtc.aar"
$source = Join-Path $PSScriptRoot 'libwebrtc.Himo-AudioFix-InPlace.aar'
$backup = "$target.before-himo-call-audio-fix-v3.bak"
$cleanSha = '4452be1b5fc8f2133602bbab06013e49e7f1425e8d967ea11bc43a1c30415db8'
$fixedSha = '764367e387ae85f42807c2570ad792e9f6f22fd7bec9ceed5a797af213f69068'

# The fixed SHA is validated from the actual file shipped with this package below.
if (-not (Test-Path -LiteralPath $source)) {
    throw "Patched AAR not found: $source"
}

$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
# Keep the check explicit so a partially copied/corrupted package is never installed.
if ($sourceHash -ne '764367E387AE85F42807C2570AD792E9F6F22FD7BEC9CEED5A797AF213F69068'.ToLowerInvariant()) {
    throw "The packaged AAR hash is unexpected: $sourceHash"
}

if (-not (Test-Path -LiteralPath $target)) {
    Write-Host 'WebRTC AAR is not present. Restoring the NuGet package first...' -ForegroundColor Yellow
    & dotnet restore '.\Himo.csproj'
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
}

if (-not (Test-Path -LiteralPath $target)) {
    throw "Target AAR was not found after restore: $target"
}

$currentSha = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Current AAR SHA256: $currentSha"

if ($currentSha -eq $sourceHash) {
    Write-Host 'V3 WebRTC audio fix is already installed.' -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $target -Destination $backup -Force
    Write-Host "Backup created: $backup"
}

$temp = "$target.himo-v3.tmp"
Copy-Item -LiteralPath $source -Destination $temp -Force
Move-Item -LiteralPath $temp -Destination $target -Force

$installedSha = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
if ($installedSha -ne $sourceHash) {
    throw "Installed AAR hash mismatch. Expected $sourceHash, got $installedSha"
}

Write-Host ''
Write-Host 'Himo Call Audio Fix V3 installed successfully.' -ForegroundColor Green
Write-Host "Target: $target"
Write-Host "SHA256: $installedSha"
Write-Host 'Next: close Visual Studio, delete .\bin and .\obj, reopen the solution, then Rebuild All.' -ForegroundColor Cyan
