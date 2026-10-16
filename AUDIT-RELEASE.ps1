$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host 'Himo release audit' -ForegroundColor Cyan

$forbidden = @('.vs','bin','obj')
foreach ($name in $forbidden) {
    $found = Get-ChildItem -Path $root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $name }
    if ($found) { throw "Forbidden build directory found: $name" }
}

$serviceAccount = Get-ChildItem -Path $root -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'service[-_]?account.*\.json$' }
if ($serviceAccount) { throw 'Firebase service-account JSON found in the project package.' }

Write-Host 'Audit passed.' -ForegroundColor Green
