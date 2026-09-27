param(
    [Parameter(Mandatory=$false)]
    [string]$ApkPath = ""
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $ApkPath = Get-ChildItem -Path "$PSScriptRoot\..\bin" -Recurse -Filter *.apk -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ApkPath -or -not (Test-Path $ApkPath)) { throw "APK not found." }
$ndkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk\ndk'
$readelf = Get-ChildItem $ndkRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | ForEach-Object {
    $candidate = Join-Path $_.FullName 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
    if (Test-Path $candidate) { $candidate; break }
}
if (-not $readelf) { throw "llvm-readelf.exe not found. Install NDK r28+." }
$temp = Join-Path ([IO.Path]::GetTempPath()) ("Himo16KB_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Expand-Archive -LiteralPath $ApkPath -DestinationPath $temp -Force
    $libs = Get-ChildItem $temp -Recurse -Filter *.so
    if (-not $libs) { throw "No native .so libraries found in APK." }
    $target = $libs | Where-Object { $_.Name -eq 'libjingle_peerconnection_so.so' -and $_.FullName -match '\\lib\\(arm64-v8a|x86_64)\\' }
    if (-not $target) { throw "libjingle_peerconnection_so.so was not found in arm64-v8a/x86_64 in the APK." }
    $bad = @()
    foreach ($lib in $target) {
        $abi = if ($lib.FullName -match '\\lib\\(arm64-v8a|x86_64)\\') { $Matches[1] } else { 'unknown' }
        $loads = @(& $readelf -lW $lib.FullName 2>$null | Select-String 'LOAD')
        $aligns = @()
        foreach ($line in $loads) {
            $parts = ($line.ToString() -split '\s+') | Where-Object { $_ -ne '' }
            if ($parts.Count -gt 0 -and $parts[-1] -match '^0x([0-9a-fA-F]+)$') { $aligns += [Convert]::ToInt64($Matches[1],16) }
        }
        if ($aligns | Where-Object { $_ -lt 0x4000 }) { $bad += $lib.FullName; Write-Host "FAIL $abi $($lib.Name): $($aligns -join ', ')" }
        else { Write-Host "OK   $abi $($lib.Name): $($aligns -join ', ')" }
    }
    if ($bad.Count) { throw "16 KB verification FAILED." }
    Write-Host "16 KB verification PASSED for libjingle_peerconnection_so.so."
}
finally { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
