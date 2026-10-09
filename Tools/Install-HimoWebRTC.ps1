[CmdletBinding()]
param([string]$Version = "144.7559.15")
$ErrorActionPreference = 'Stop'

$androidSdkRoot = 'C:\Program Files (x86)\Android\android-sdk'
$androidJar = Join-Path $androidSdkRoot 'platforms\android-36\android.jar'
if (!(Test-Path $androidJar)) { throw "Android 36 android.jar not found: $androidJar" }

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

$javacCommand = Get-Command javac.exe -ErrorAction SilentlyContinue
$javac = $null
if ($null -ne $javacCommand) { $javac = $javacCommand.Source }
if (!$javac -and $env:JAVA_HOME) {
    $candidate = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    if (Test-Path $candidate) { $javac = $candidate }
}
if (!$javac) { throw "javac.exe was not found. Install/use a JDK 17+ and make javac available." }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceJava = Join-Path $root 'WebRtcAudioManager.java'
if (!(Test-Path $sourceJava)) { throw "Missing WebRtcAudioManager.java: $sourceJava" }

$packageAar = Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15\lib\net10.0-android36.0\libwebrtc.aar'
if (!(Test-Path $packageAar)) { throw "FsWebRTC package AAR was not found: $packageAar" }

$cacheRoot = Join-Path $env:LOCALAPPDATA 'Himo\WebRTC16KB'
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$sourceAar = Join-Path $cacheRoot "android-$Version-full.aar"
$url = "https://repo1.maven.org/maven2/io/github/webrtc-sdk/android/$Version/android-$Version.aar"

if (!(Test-Path $sourceAar) -or (Get-Item $sourceAar).Length -lt 1000000) {
    Write-Host "Downloading WebRTC Android AAR $Version..."
    Invoke-WebRequest -Uri $url -OutFile $sourceAar -UseBasicParsing
}
if ((Get-Item $sourceAar).Length -lt 1000000) { throw "Invalid downloaded WebRTC AAR: $sourceAar" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Copy-ZipEntry([System.IO.Compression.ZipArchiveEntry]$src, [System.IO.Compression.ZipArchive]$dst, [string]$name) {
    $out = $dst.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $srcStream = $src.Open()
    $dstStream = $out.Open()
    try { $srcStream.CopyTo($dstStream) } finally { $dstStream.Dispose(); $srcStream.Dispose() }
}

function Copy-ZipFileReplacingEntry([string]$inputZipPath, [string]$outputZipPath, [string]$replaceName, [string]$replacementPath) {
    if (Test-Path $outputZipPath) { Remove-Item $outputZipPath -Force }
    $inStream = [IO.File]::OpenRead($inputZipPath)
    $inZip = New-Object System.IO.Compression.ZipArchive($inStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
    $outStream = [IO.File]::Create($outputZipPath)
    $outZip = New-Object System.IO.Compression.ZipArchive($outStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($entry in $inZip.Entries) {
            if ($entry.FullName -eq $replaceName) { continue }
            Copy-ZipEntry $entry $outZip $entry.FullName
        }
        $newEntry = $outZip.CreateEntry($replaceName, [System.IO.Compression.CompressionLevel]::Optimal)
        $src = [IO.File]::OpenRead($replacementPath)
        $dst = $newEntry.Open()
        try { $src.CopyTo($dst) } finally { $dst.Dispose(); $src.Dispose() }
    } finally {
        $outZip.Dispose(); $outStream.Dispose(); $inZip.Dispose(); $inStream.Dispose()
    }
}

function Assert-16KB([string]$so, [string]$abi) {
    $loads = @(& $readelf -lW $so 2>$null | Select-String '\sLOAD\s')
    if (!$loads) { throw "No ELF LOAD segments found: $abi" }
    foreach ($line in $loads) {
        $parts = ($line.ToString() -split '\s+') | Where-Object { $_ -ne '' }
        $align = $parts[-1]
        if ($align -notmatch '^0x([0-9a-fA-F]+)$') { throw "Unable to read ELF alignment for ${abi}: $line" }
        if ([Convert]::ToInt64($Matches[1], 16) -lt 0x4000) { throw "Not 16 KB ELF aligned for ${abi}: $align" }
    }
    Write-Host "OK: $abi 16 KB ELF alignment"
}

function Assert-Entry([System.IO.Compression.ZipArchive]$zip, [string]$entryName) {
    if (-not $zip.GetEntry($entryName)) { throw "Required entry missing: $entryName" }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("HimoWebRTCAudio_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
try {
    # Extract source AAR classes.jar and native libraries.
    $aarStream = [IO.File]::OpenRead($sourceAar)
    $aar = New-Object System.IO.Compression.ZipArchive($aarStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
    try {
        Assert-Entry $aar 'classes.jar'
        Assert-Entry $aar 'jni/arm64-v8a/libjingle_peerconnection_so.so'
        Assert-Entry $aar 'jni/x86_64/libjingle_peerconnection_so.so'

        $classesJar = Join-Path $temp 'classes.jar'
        $ce = $aar.GetEntry('classes.jar')
        $cs = $ce.Open(); $co = [IO.File]::Create($classesJar)
        try { $cs.CopyTo($co) } finally { $co.Dispose(); $cs.Dispose() }

        $classOut = Join-Path $temp 'patch-classes'
        New-Item -ItemType Directory -Force -Path $classOut | Out-Null
        & $javac -source 8 -target 8 -classpath $androidJar -d $classOut $sourceJava
        if ($LASTEXITCODE -ne 0) { throw "javac failed while building Himo WebRTC audio manager." }

        $compiledClass = Join-Path $classOut 'org\webrtc\audio\WebRtcAudioManager.class'
        if (!(Test-Path $compiledClass)) { throw "Compiled WebRtcAudioManager.class was not produced." }

        # Replace only the problematic Java helper. Java + native WebRTC remain
        # from the exact same Maven AAR revision.
        $patchedClasses = Join-Path $temp 'classes-patched.jar'
        Copy-ZipFileReplacingEntry $classesJar $patchedClasses 'org/webrtc/audio/WebRtcAudioManager.class' $compiledClass

        # Verify the patched class is present.
        $verifyStream = [IO.File]::OpenRead($patchedClasses)
        $verifyZip = New-Object System.IO.Compression.ZipArchive($verifyStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            Assert-Entry $verifyZip 'org/webrtc/PeerConnectionFactory.class'
            Assert-Entry $verifyZip 'org/webrtc/audio/JavaAudioDeviceModule.class'
            Assert-Entry $verifyZip 'org/webrtc/audio/WebRtcAudioManager.class'
            Assert-Entry $verifyZip 'org/jni_zero/JniInit.class'
            Write-Host "OK: unified WebRTC Java classes + Himo safe WebRtcAudioManager"
        } finally { $verifyZip.Dispose(); $verifyStream.Dispose() }
    } finally { $aar.Dispose(); $aarStream.Dispose() }

    foreach ($abi in @('arm64-v8a','x86_64')) {
        $nativePath = Join-Path $temp "$abi-libjingle_peerconnection_so.so"
        $s = [IO.File]::OpenRead($sourceAar)
        $z = New-Object System.IO.Compression.ZipArchive($s, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            $e = $z.GetEntry("jni/$abi/libjingle_peerconnection_so.so")
            $o = [IO.File]::Create($nativePath); $i = $e.Open()
            try { $i.CopyTo($o) } finally { $o.Dispose(); $i.Dispose() }
        } finally { $z.Dispose(); $s.Dispose() }
        Assert-16KB $nativePath $abi
    }

    # Rebuild the whole AAR with the patched class; never mix Java/native revisions.
    $patchedAar = Join-Path $temp "android-$Version-himo-audio.aar"
    Copy-ZipFileReplacingEntry $sourceAar $patchedAar 'classes.jar' $patchedClasses
    Copy-Item $patchedAar $packageAar -Force

    Write-Host "Installed Himo unified WebRTC AAR: $packageAar"
    Write-Host "Audio manager now validates Android buffer sizes before WebRTC AudioParameters uses them."
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
