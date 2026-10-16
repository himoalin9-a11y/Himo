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
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectAar = Join-Path $projectRoot 'Platforms\Android\Jars\libwebrtc.aar'
if (!(Test-Path $packageAar)) { throw "FsWebRTC package AAR was not found: $packageAar" }

$cacheRoot = Join-Path $env:LOCALAPPDATA 'Himo\WebRTC16KB'
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$cachedAar = Join-Path $cacheRoot "android-$Version-full.aar"
$url = "https://repo1.maven.org/maven2/io/github/webrtc-sdk/android/$Version/android-$Version.aar"

if (!(Test-Path $cachedAar) -or (Get-Item $cachedAar).Length -lt 1000000) {
    Write-Host "Downloading unified WebRTC Android AAR $Version..."
    Invoke-WebRequest -Uri $url -OutFile $cachedAar -UseBasicParsing
}

if ((Get-Item $cachedAar).Length -lt 1000000) {
    throw "Invalid downloaded WebRTC AAR: $cachedAar"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("HimoWebRTCUnified_" + [guid]::NewGuid().ToString('N'))
$extractDir = Join-Path $temp 'aar'
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-16KB([string]$so, [string]$abi) {
    $loads = @(& $readelf -lW $so 2>$null | Select-String '\sLOAD\s')
    if (!$loads) { throw "No ELF LOAD segments found: $abi" }

    foreach ($line in $loads) {
        $parts = ($line.ToString() -split '\s+') | Where-Object { $_ -ne '' }
        $align = $parts[-1]
        if ($align -notmatch '^0x([0-9a-fA-F]+)$') {
            throw "Unable to read ELF alignment for ${abi}: $line"
        }
        if ([Convert]::ToInt64($Matches[1], 16) -lt 0x4000) {
            throw "Not 16 KB ELF aligned for ${abi}: $align"
        }
    }
    Write-Host "OK: $abi is 16 KB ELF aligned."
}

function Assert-ZipEntry([System.IO.Compression.ZipArchive]$zip, [string]$entryName) {
    if (-not $zip.GetEntry($entryName)) {
        throw "Required AAR entry missing: $entryName"
    }
}

try {
    $sourceStream = [System.IO.File]::OpenRead($cachedAar)
    $sourceZip = New-Object System.IO.Compression.ZipArchive($sourceStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)

    Assert-ZipEntry $sourceZip 'classes.jar'
    Assert-ZipEntry $sourceZip 'jni/arm64-v8a/libjingle_peerconnection_so.so'
    Assert-ZipEntry $sourceZip 'jni/x86_64/libjingle_peerconnection_so.so'

    $classesJarEntry = $sourceZip.GetEntry('classes.jar')
    $classesJarPath = Join-Path $temp 'classes.jar'
    $classesOut = [System.IO.File]::Create($classesJarPath)
    try {
        $in = $classesJarEntry.Open()
        try { $in.CopyTo($classesOut) } finally { $in.Dispose() }
    } finally { $classesOut.Dispose() }

    $classesStream = [System.IO.File]::OpenRead($classesJarPath)
    $classesZip = New-Object System.IO.Compression.ZipArchive($classesStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)

    foreach ($entry in @(
        'org/webrtc/PeerConnectionFactory.class',
        'org/webrtc/PeerConnection.class',
        'org/webrtc/audio/JavaAudioDeviceModule.class',
        'org/webrtc/audio/WebRtcAudioManager.class',
        'org/jni_zero/JniInit.class'
    )) {
        Assert-ZipEntry $classesZip $entry
    }

    Write-Host "OK: unified Java WebRTC classes verified for $Version."

    foreach ($abi in @('arm64-v8a','x86_64')) {
        $soEntry = $sourceZip.GetEntry("jni/$abi/libjingle_peerconnection_so.so")
        $soPath = Join-Path $temp "$abi-libjingle_peerconnection_so.so"
        $soOut = [System.IO.File]::Create($soPath)
        try {
            $in = $soEntry.Open()
            try { $in.CopyTo($soOut) } finally { $in.Dispose() }
        } finally { $soOut.Dispose() }
        Assert-16KB $soPath $abi
    }

    $classesZip.Dispose()
    $classesStream.Dispose()
    $sourceZip.Dispose()
    $sourceStream.Dispose()

    # IMPORTANT: replace the entire AAR, not only libjingle_peerconnection_so.so.
    # This keeps Java API/classes, JNI bridge classes, and native code on the same
    # WebRTC revision. The FsWebRTC NuGet package's existing binding transforms
    # remain in the package and are still applied by the Android binding build.
    Copy-Item $cachedAar $packageAar -Force
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $projectAar) | Out-Null
    Copy-Item $cachedAar $projectAar -Force

    Write-Host "Installed unified WebRTC $Version AAR into NuGet cache: $packageAar"
    Write-Host "Staged the same verified AAR for the project build: $projectAar"
    Write-Host "Java + native WebRTC are now from the same AAR revision."
    Write-Host "Do not run the older script that replaces only the .so files."
}
finally {
    if (Test-Path $temp) {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
