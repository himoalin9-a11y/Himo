[CmdletBinding()]
param([string]$Version = "144.7559.15")
$ErrorActionPreference = 'Stop'

$androidSdkRoot = 'C:\Program Files (x86)\Android\android-sdk'
$ndkCandidates = @(
    (Join-Path $androidSdkRoot 'ndk\28.2.13676358'),
    (Join-Path $androidSdkRoot 'ndk-bundle')
)

$readelf = $null
foreach ($ndk in $ndkCandidates) {
    $candidate = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
    if (Test-Path $candidate) { $readelf = $candidate; break }
}
if (!$readelf) { throw "llvm-readelf.exe was not found in the expected Android NDK locations." }

$packageAar = Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15\lib\net10.0-android36.0\libwebrtc.aar'
if (!(Test-Path $packageAar)) { throw "FsWebRTC package AAR was not found: $packageAar" }

$backup = "$packageAar.before-16kb-jniinit.bak"
if (!(Test-Path $backup)) {
    throw "The original FsWebRTC AAR backup was not found: $backup. Reinstall package 0.9.3.15 first, then run this script."
}

# Always start from the untouched original AAR to remove any malformed previous patch.
Copy-Item $backup $packageAar -Force

$cacheRoot = Join-Path $env:LOCALAPPDATA 'Himo\WebRTC16KB'
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$cachedAar = Join-Path $cacheRoot "android-$Version.aar"
$url = "https://repo1.maven.org/maven2/io/github/webrtc-sdk/android/$Version/android-$Version.aar"
if (!(Test-Path $cachedAar)) {
    Invoke-WebRequest -Uri $url -OutFile $cachedAar -UseBasicParsing
}
if ((Get-Item $cachedAar).Length -lt 1000000) { throw "Invalid downloaded WebRTC AAR: $cachedAar" }

$temp = Join-Path ([IO.Path]::GetTempPath()) ("HimoWebRTC16KB_Fixed_" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $temp 'source'
$targetDir = Join-Path $temp 'target'
New-Item -ItemType Directory -Force -Path $sourceDir, $targetDir | Out-Null

function Assert-16KB([string]$so, [string]$abi) {
    $loads = @(& $readelf -lW $so 2>$null | Select-String '\sLOAD\s')
    if (!$loads) { throw "No ELF LOAD segments found: $abi" }
    $bad = @()
    foreach ($line in $loads) {
        $parts = ($line.ToString() -split '\s+') | Where-Object { $_ -ne '' }
        $align = $parts[-1]
        if ($align -match '^0x([0-9a-fA-F]+)$') {
            if ([Convert]::ToInt64($Matches[1], 16) -lt 0x4000) { $bad += $align }
        } else {
            throw "Unable to read ELF alignment for ${abi}: $line"
        }
    }
    if ($bad.Count) { throw "Not 16 KB ELF aligned for ${abi}: $($bad -join ', ')" }
    Write-Host "OK: $abi is 16 KB ELF aligned."
}

try {
    # Extract source AAR for native library and JniInit.class only.
    $sourceZip = Join-Path $temp 'source.zip'
    Copy-Item $cachedAar $sourceZip -Force
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $sourceDir -Force

    # Extract the pristine original FsWebRTC AAR.
    $targetZip = Join-Path $temp 'target.zip'
    Copy-Item $packageAar $targetZip -Force
    Expand-Archive -LiteralPath $targetZip -DestinationPath $targetDir -Force

    foreach ($abi in 'arm64-v8a', 'x86_64') {
        $sourceSo = Join-Path $sourceDir "jni\$abi\libjingle_peerconnection_so.so"
        $targetSo = Join-Path $targetDir "jni\$abi\libjingle_peerconnection_so.so"
        if (!(Test-Path $sourceSo)) { throw "Downloaded WebRTC AAR is missing $abi/libjingle_peerconnection_so.so" }
        if (!(Test-Path $targetSo)) { throw "FsWebRTC AAR is missing $abi/libjingle_peerconnection_so.so" }
        Assert-16KB $sourceSo $abi
        Copy-Item $sourceSo $targetSo -Force
    }

    # Patch the existing classes.jar in-place so ALL original org.webrtc classes remain intact.
    $sourceClassesJar = Join-Path $sourceDir 'classes.jar'
    $targetClassesJar = Join-Path $targetDir 'classes.jar'
    if (!(Test-Path $sourceClassesJar)) { throw "Source WebRTC AAR is missing classes.jar" }
    if (!(Test-Path $targetClassesJar)) { throw "FsWebRTC AAR is missing classes.jar" }

    $sourceClassesDir = Join-Path $temp 'source-classes'
    New-Item -ItemType Directory -Force -Path $sourceClassesDir | Out-Null
    $sourceClassesZip = Join-Path $temp 'source-classes.zip'
    Copy-Item $sourceClassesJar $sourceClassesZip -Force
    Expand-Archive -LiteralPath $sourceClassesZip -DestinationPath $sourceClassesDir -Force

    $jniInit = Join-Path $sourceClassesDir 'org\jni_zero\JniInit.class'
    if (!(Test-Path $jniInit)) { throw "Source WebRTC AAR does not contain org/jni_zero/JniInit.class" }

    # Patch classes.jar with .NET ZipArchive so no JDK/jar.exe is required.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceJniBytes = [System.IO.File]::ReadAllBytes($jniInit)
    $fileStream = $null
    $zip = $null
    try {
        $fileStream = [System.IO.File]::Open($targetClassesJar, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $zip = New-Object System.IO.Compression.ZipArchive($fileStream, [System.IO.Compression.ZipArchiveMode]::Update, $false)

        $entryName = 'org/jni_zero/JniInit.class'
        $existing = $zip.GetEntry($entryName)
        if ($existing) { $existing.Delete() }
        $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $null
        try {
            $entryStream = $entry.Open()
            $entryStream.Write($sourceJniBytes, 0, $sourceJniBytes.Length)
        } finally {
            if ($entryStream) { $entryStream.Dispose() }
        }
    } finally {
        if ($zip) { $zip.Dispose() }
        if ($fileStream) { $fileStream.Dispose() }
    }

    # Sanity-check that the original org.webrtc package is still present.
    $checkStream = $null
    $checkZip = $null
    try {
        $checkStream = [System.IO.File]::OpenRead($targetClassesJar)
        $checkZip = New-Object System.IO.Compression.ZipArchive($checkStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        if (-not $checkZip.GetEntry('org/webrtc/PeerConnection.class')) {
            throw "Patched classes.jar does not contain org/webrtc/PeerConnection.class; refusing to install malformed AAR."
        }
        if (-not $checkZip.GetEntry('org/webrtc/PeerConnectionFactory.class')) {
            throw "Patched classes.jar does not contain org/webrtc/PeerConnectionFactory.class; refusing to install malformed AAR."
        }
        if (-not $checkZip.GetEntry('org/jni_zero/JniInit.class')) {
            throw "Patched classes.jar does not contain org/jni_zero/JniInit.class; refusing to install malformed AAR."
        }
    } finally {
        if ($checkZip) { $checkZip.Dispose() }
        if ($checkStream) { $checkStream.Dispose() }
    }

    $patchedAarZip = Join-Path $temp 'libwebrtc-patched.zip'
    Compress-Archive -Path (Join-Path $targetDir '*') -DestinationPath $patchedAarZip -Force
    $patchedAar = Join-Path $temp 'libwebrtc-patched.aar'
    Move-Item $patchedAarZip $patchedAar -Force
    Copy-Item $patchedAar $packageAar -Force

    # Stage the exact patched AAR inside the Himo project. The Android build then
    # consumes this concrete file instead of relying on NuGet transitive Android
    # asset propagation.
    $projectJars = Join-Path $PSScriptRoot '..\Platforms\Android\Jars'
    New-Item -ItemType Directory -Force -Path $projectJars | Out-Null
    $projectAar = Join-Path $projectJars 'libwebrtc.aar'
    Copy-Item $patchedAar $projectAar -Force

    Write-Host "Patched 16 KB native WebRTC libraries while preserving original FsWebRTC Java bindings."
    Write-Host "Added org.jni_zero.JniInit.class from WebRTC $Version."
    Write-Host "Verified org.webrtc.PeerConnection.class and org.webrtc.PeerConnectionFactory.class remain present."
    Write-Host "Installed NuGet AAR: $packageAar"
    Write-Host "Staged project AAR: $projectAar"
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
