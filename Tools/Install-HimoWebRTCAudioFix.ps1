[CmdletBinding()]
param(
    [switch]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$nugetAar = Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15\lib\net10.0-android36.0\libwebrtc.aar'
$unifiedAar = Join-Path $env:LOCALAPPDATA 'Himo\WebRTC16KB\android-144.7559.15-full.aar'
$patchClass = Join-Path $PSScriptRoot 'WebRtcAudioFix\WebRtcAudioManager.class'
$backupAar = "$nugetAar.before-himo-audio-safe.aar"

if (!(Test-Path $nugetAar)) {
    throw "FsWebRTC package AAR not found: $nugetAar"
}

if ($RestoreBackup) {
    if (!(Test-Path $backupAar)) {
        throw "Backup AAR was not found: $backupAar"
    }
    Copy-Item $backupAar $nugetAar -Force
    Write-Host "Restored original AAR backup: $nugetAar"
    exit 0
}

if (!(Test-Path $patchClass)) {
    throw "Precompiled WebRtcAudioManager.class not found: $patchClass"
}

# Prefer the already-downloaded unified WebRTC AAR produced by the previous
# 16 KB setup. This avoids requiring javac, JDK, or an internet download.
$sourceAar = $null
if (Test-Path $unifiedAar) {
    $sourceAar = $unifiedAar
} else {
    $sourceAar = $nugetAar
    Write-Warning "Unified M144 AAR was not found at $unifiedAar. The current NuGet AAR will be patched in-place. Verify your existing 16 KB WebRTC setup before rebuilding."
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HimoWebRTCAudioFix_" + [guid]::NewGuid().ToString('N'))
$aarDir = Join-Path $tempRoot 'aar'
$classesDir = Join-Path $tempRoot 'classes'
$newClassesJar = Join-Path $tempRoot 'classes.jar'
$newAar = Join-Path $tempRoot 'libwebrtc.aar'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Entry([string]$zipPath, [string]$entryName) {
    $stream = [IO.File]::OpenRead($zipPath)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            if (-not $zip.GetEntry($entryName)) {
                throw "Required archive entry missing: $entryName"
            }
        } finally {
            $zip.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Get-EntryBytes([string]$zipPath, [string]$entryName) {
    $stream = [IO.File]::OpenRead($zipPath)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            $entry = $zip.GetEntry($entryName)
            if (-not $entry) { throw "Archive entry missing: $entryName" }
            $entryStream = $entry.Open()
            $memory = New-Object IO.MemoryStream
            try { $entryStream.CopyTo($memory); return $memory.ToArray() }
            finally { $entryStream.Dispose(); $memory.Dispose() }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

function Write-ZipFromDirectory([string]$directory, [string]$destination) {
    if (Test-Path $destination) { Remove-Item $destination -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $directory,
        $destination,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

try {
    New-Item -ItemType Directory -Force -Path $aarDir | Out-Null
    New-Item -ItemType Directory -Force -Path $classesDir | Out-Null

    foreach ($entry in @(
        'classes.jar',
        'jni/arm64-v8a/libjingle_peerconnection_so.so',
        'jni/x86_64/libjingle_peerconnection_so.so'
    )) {
        Assert-Entry $sourceAar $entry
    }

    $sourceStream = [IO.File]::OpenRead($sourceAar)
    try {
        $sourceZip = New-Object System.IO.Compression.ZipArchive($sourceStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            foreach ($entry in $sourceZip.Entries) {
                $target = Join-Path $aarDir $entry.FullName
                if ($entry.FullName.EndsWith('/')) {
                    New-Item -ItemType Directory -Force -Path $target | Out-Null
                    continue
                }
                $parent = Split-Path -Parent $target
                if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
                $input = $entry.Open()
                $output = [IO.File]::Create($target)
                try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
            }
        } finally { $sourceZip.Dispose() }
    } finally { $sourceStream.Dispose() }

    $classesJar = Join-Path $aarDir 'classes.jar'
    if (!(Test-Path $classesJar)) { throw 'classes.jar was not extracted.' }

    Expand-Archive -LiteralPath $classesJar -DestinationPath $classesDir -Force

    $targetClass = Join-Path $classesDir 'org\webrtc\audio\WebRtcAudioManager.class'
    $targetParent = Split-Path -Parent $targetClass
    New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
    Copy-Item $patchClass $targetClass -Force

    # The replacement class is precompiled as Java 8 bytecode and contains only
    # Android API references, so no JDK/javac is needed on the user's machine.
    Write-ZipFromDirectory $classesDir $newClassesJar
    Copy-Item $newClassesJar $classesJar -Force

    # Verify the patched classes.jar before rebuilding the AAR.
    Assert-Entry $classesJar 'org/webrtc/PeerConnectionFactory.class'
    Assert-Entry $classesJar 'org/webrtc/audio/JavaAudioDeviceModule.class'
    Assert-Entry $classesJar 'org/webrtc/audio/WebRtcAudioManager.class'

    if (!(Test-Path $backupAar)) {
        Copy-Item $nugetAar $backupAar -Force
        Write-Host "Created one-time AAR backup: $backupAar"
    }

    Write-ZipFromDirectory $aarDir $newAar
    Copy-Item $newAar $nugetAar -Force

    $arm64Before = [Convert]::ToBase64String((Get-EntryBytes $sourceAar 'jni/arm64-v8a/libjingle_peerconnection_so.so'))
    $arm64After  = [Convert]::ToBase64String((Get-EntryBytes $nugetAar 'jni/arm64-v8a/libjingle_peerconnection_so.so'))
    $x64Before = [Convert]::ToBase64String((Get-EntryBytes $sourceAar 'jni/x86_64/libjingle_peerconnection_so.so'))
    $x64After  = [Convert]::ToBase64String((Get-EntryBytes $nugetAar 'jni/x86_64/libjingle_peerconnection_so.so'))

    if ($arm64Before -ne $arm64After) { throw 'arm64 WebRTC native library changed unexpectedly.' }
    if ($x64Before -ne $x64After) { throw 'x86_64 WebRTC native library changed unexpectedly.' }

    Write-Host ''
    Write-Host 'Himo WebRTC audio fix installed successfully.'
    Write-Host 'Java/native WebRTC binaries were preserved unchanged.'
    Write-Host 'Only org/webrtc/audio/WebRtcAudioManager.class was replaced.'
    Write-Host 'Low-latency OEM buffer properties are bypassed; AudioTrack/AudioRecord min-buffer sizing is used instead.'
    Write-Host "Patched AAR: $nugetAar"
    Write-Host ''
    Write-Host 'Next: Clean -> delete Himo bin/obj -> Rebuild -> install APK.'
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
