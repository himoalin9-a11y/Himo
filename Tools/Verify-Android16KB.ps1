[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ApkPath = ""
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $binPath = Join-Path $PSScriptRoot '..\bin'
    $apk = Get-ChildItem -Path $binPath -Recurse -Filter *.apk -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($apk) { $ApkPath = $apk.FullName }
}
if ([string]::IsNullOrWhiteSpace($ApkPath) -or -not (Test-Path -LiteralPath $ApkPath)) {
    throw "APK not found. Build Himo first, or pass -ApkPath with the APK full path."
}

# Support the common Visual Studio Android SDK locations, including the SDK path used by Himo.
$sdkRoots = @(
    $env:ANDROID_SDK_ROOT,
    $env:ANDROID_HOME,
    'C:\Program Files (x86)\Android\android-sdk',
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$readelf = $null
foreach ($sdkRoot in $sdkRoots) {
    $ndkRoot = Join-Path $sdkRoot 'ndk'
    if (Test-Path $ndkRoot) {
        foreach ($ndk in (Get-ChildItem $ndkRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)) {
            $candidate = Join-Path $ndk.FullName 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
            if (Test-Path $candidate) { $readelf = $candidate; break }
        }
    }
    if ($readelf) { break }
    $bundleCandidate = Join-Path $sdkRoot 'ndk-bundle\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
    if (Test-Path $bundleCandidate) { $readelf = $bundleCandidate; break }
}
if (-not $readelf) {
    throw "llvm-readelf.exe not found. Checked ANDROID_SDK_ROOT, ANDROID_HOME, C:\Program Files (x86)\Android\android-sdk, and LOCALAPPDATA Android SDK paths."
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("Himo16KB_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $apkZip = Join-Path $temp 'Himo.apk.zip'
Copy-Item -LiteralPath $ApkPath -Destination $apkZip -Force
Expand-Archive -LiteralPath $apkZip -DestinationPath $temp -Force
    $targetLibs = @()
    foreach ($abi in @('arm64-v8a', 'x86_64')) {
        $lib = Join-Path $temp ("lib\{0}\libjingle_peerconnection_so.so" -f $abi)
        if (Test-Path -LiteralPath $lib) { $targetLibs += [pscustomobject]@{ Abi = $abi; Path = $lib } }
    }
    if (-not $targetLibs) {
        throw "libjingle_peerconnection_so.so not found under lib\arm64-v8a or lib\x86_64 in APK: $ApkPath"
    }

    $bad = @()
    foreach ($item in $targetLibs) {
        $loadLines = @(& $readelf -lW $item.Path 2>$null | Select-String '\sLOAD\s')
        if (-not $loadLines) { throw "No ELF LOAD segments found for $($item.Abi)." }
        $alignments = @()
        foreach ($line in $loadLines) {
            $parts = ($line.ToString() -split '\s+') | Where-Object { $_ -ne '' }
            if (-not $parts -or $parts[-1] -notmatch '^0x([0-9a-fA-F]+)$') {
                throw "Could not parse ELF LOAD alignment for $($item.Abi): $line"
            }
            $alignments += [Convert]::ToInt64($Matches[1], 16)
        }
        $formatted = ($alignments | ForEach-Object { '0x{0:X}' -f $_ }) -join ', '
        if ($alignments | Where-Object { $_ -lt 0x4000 }) {
            $bad += $item.Abi
            Write-Host "FAIL $($item.Abi): LOAD alignments $formatted (one or more are below 16 KB)"
        }
        else {
            Write-Host "OK   $($item.Abi): LOAD alignments $formatted"
        }
    }
    if ($bad.Count) { throw "16 KB verification FAILED for: $($bad -join ', '). APK: $ApkPath" }
    Write-Host "16 KB verification PASSED for libjingle_peerconnection_so.so."
    Write-Host "APK: $ApkPath"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
