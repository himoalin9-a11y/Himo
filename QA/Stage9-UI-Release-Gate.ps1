$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$views = Join-Path $root 'Views'

Write-Host 'Himo Stage 9 - UI Release Gate'

$failed = $false

foreach ($xaml in Get-ChildItem $views -Filter '*.xaml') {
    $content = Get-Content $xaml.FullName -Raw
    $names = [regex]::Matches($content, 'x:Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    $dupes = $names | Group-Object | Where-Object Count -gt 1
    if ($dupes) {
        Write-Host "FAIL duplicate x:Name in $($xaml.Name): $($dupes.Name -join ', ')"
        $failed = $true
    }

    $handlers = [regex]::Matches($content, '(?:Clicked|Completed|SelectionChanged|Refreshing|TextChanged)="([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $codeBehind = [System.IO.Path]::ChangeExtension($xaml.FullName, '.xaml.cs')
    if (Test-Path $codeBehind) {
        $cs = Get-Content $codeBehind -Raw
        foreach ($handler in $handlers) {
            if ($handler -and $cs -notmatch "\b$([regex]::Escape($handler))\s*\(") {
                Write-Host "FAIL missing handler '$handler' in $([System.IO.Path]::GetFileName($codeBehind))"
                $failed = $true
            }
        }
    }
}

$csproj = Join-Path $root 'Himo.csproj'
$project = Get-Content $csproj -Raw
foreach ($item in @('Himo.Api\\**', 'Program.cs', 'FcmPushService.cs', 'EmailVerificationService.cs')) {
    if ($project -notmatch [regex]::Escape($item)) {
        Write-Host "WARN expected exclusion not found in Himo.csproj: $item"
    }
}

if ($failed) {
    Write-Host 'Stage 9 gate: FAILED'
    exit 1
}

Write-Host 'Stage 9 gate: PASSED (static checks only)'
