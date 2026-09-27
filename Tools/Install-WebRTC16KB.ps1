[CmdletBinding()]
param([string]$Version = "144.7559.15")
$ErrorActionPreference = 'Stop'

$androidSdkRoot = 'C:\Program Files (x86)\Android\android-sdk'
$ndkRoot = Join-Path $androidSdkRoot 'ndk-bundle'
$readelf = Join-Path $ndkRoot 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
$packageAar = Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15\lib\net10.0-android36.0\libwebrtc.aar'

if (!(Test-Path $packageAar)) { throw "FsWebRTC package AAR was not found: $packageAar" }
if (!(Test-Path $readelf)) { throw "llvm-readelf.exe was not found: $readelf" }

$cacheRoot = Join-Path $env:LOCALAPPDATA 'Himo\WebRTC16KB'
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$cachedAar = Join-Path $cacheRoot "android-$Version.aar"
$url = "https://repo1.maven.org/maven2/io/github/webrtc-sdk/android/$Version/android-$Version.aar"
if (!(Test-Path $cachedAar)) {
    Invoke-WebRequest -Uri $url -OutFile $cachedAar -UseBasicParsing
}
if ((Get-Item $cachedAar).Length -lt 1000000) { throw "Invalid downloaded WebRTC AAR: $cachedAar" }

$temp = Join-Path ([IO.Path]::GetTempPath()) ("HimoWebRTC16KB_" + [guid]::NewGuid().ToString('N'))
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
            throw "Unable to read ELF alignment for $abi: $line"
        }
    }
    if ($bad.Count) { throw "Not 16 KB ELF aligned for ${abi}: $($bad -join ', ')" }
    Write-Host "OK: $abi is 16 KB ELF aligned."
}

try {
    # Extract the downloaded WebRTC only to obtain its native libraries.
    $sourceZip = Join-Path $temp 'source.zip'
    Copy-Item $cachedAar $sourceZip -Force
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $sourceDir -Force

    # Extract the original FsWebRTC AAR so its Java/classes/resources remain untouched.
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

    $backup = "$packageAar.before-16kb.bak"
    if (!(Test-Path $backup)) { Copy-Item $packageAar $backup -Force }

    $patchedAar = Join-Path $temp 'libwebrtc-patched.aar'
    Compress-Archive -Path (Join-Path $targetDir '*') -DestinationPath $patchedAar -Force
    Copy-Item $patchedAar $packageAar -Force

    Write-Host "Patched only native WebRTC libraries in: $packageAar"
    Write-Host "Original Java/resources from FsWebRTC 0.9.3.15 were preserved."
    Write-Host "Native source: WebRTC $Version"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
