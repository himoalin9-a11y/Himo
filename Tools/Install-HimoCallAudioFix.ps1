[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$aarPath = Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15\lib\net10.0-android36.0\libwebrtc.aar'
$cleanSha256 = '4452be1b5fc8f2133602bbab06013e49e7f1425e8d967ea11bc43a1c30415db8'
$fixedAar = Join-Path $PSScriptRoot 'libwebrtc.Himo-AudioFix-STORE.aar'
$fixedSha256 = '9df6271dab248711e49c7c5426d5dd2eaad94bbd94499f02db040d59485e14bf'
$backupPath = "$aarPath.before-himo-call-audio-fix.bak"

if (!(Test-Path $aarPath)) {
    throw "FsWebRTC AAR not found: $aarPath. Run 'dotnet restore .\Himo.csproj' first."
}
if (!(Test-Path $fixedAar)) {
    throw "Fixed AAR is missing: $fixedAar"
}

function Get-Sha256([string]$path) {
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$currentSha = Get-Sha256 $aarPath

# If an earlier failed/old patch changed the current AAR, prefer the known-clean
# backup created by the previous audio-fix script. Otherwise require a clean restore.
if ($currentSha -ne $cleanSha256) {
    if ((Test-Path $backupPath) -and ((Get-Sha256 $backupPath) -eq $cleanSha256)) {
        Copy-Item $backupPath $aarPath -Force
        $currentSha = Get-Sha256 $aarPath
        Write-Host 'Restored the known-clean FsWebRTC AAR from the previous backup.' -ForegroundColor Yellow
    }
}

if ($currentSha -ne $cleanSha256) {
    throw "The current NuGet AAR is not the clean 0.9.3.15 package expected by this fix. Run 'dotnet restore .\Himo.csproj' after removing the package folder, then rerun this script. Current SHA256=$currentSha"
}

$fixedSha = Get-Sha256 $fixedAar
if ($fixedSha -ne $fixedSha256) {
    throw "The bundled fixed AAR failed integrity verification. SHA256=$fixedSha"
}

# Keep a clean backup before installation.
$finalBackup = "$aarPath.before-himo-call-audio-fix-v2.bak"
Copy-Item $aarPath $finalBackup -Force
Copy-Item $fixedAar $aarPath -Force

$installedSha = Get-Sha256 $aarPath
if ($installedSha -ne $fixedSha256) {
    Copy-Item $finalBackup $aarPath -Force
    throw 'Installed AAR integrity verification failed; original AAR was restored.'
}

Write-Host ''
Write-Host 'Himo call audio fix v2 installed successfully.' -ForegroundColor Green
Write-Host "AAR: $aarPath"
Write-Host "Backup: $finalBackup"
Write-Host 'WebRTC Java classes were preserved; only WebRtcAudioManager.class is patched.'
Write-Host 'All AAR and classes.jar entries retain the original STORE layout.'
Write-Host 'Native libjingle_peerconnection_so.so files are byte-for-byte unchanged.'
