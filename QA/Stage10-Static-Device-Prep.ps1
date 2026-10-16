$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$views = Join-Path $root 'Views'
$failed = $false
Write-Host 'Himo Stage 10 - Static Device Prep'

foreach ($xaml in Get-ChildItem $views -Filter '*.xaml') {
    $content = Get-Content $xaml.FullName -Raw
    $names = [regex]::Matches($content, 'x:Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    $dupes = $names | Group-Object | Where-Object Count -gt 1
    if ($dupes) { Write-Host "FAIL duplicate x:Name: $($xaml.Name) -> $($dupes.Name -join ', ')"; $failed = $true }

    $handlers = [regex]::Matches($content, '(?:Clicked|Completed|SelectionChanged|Refreshing|TextChanged|Toggled)="([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $codeBehind = [System.IO.Path]::ChangeExtension($xaml.FullName, '.xaml.cs')
    if (Test-Path $codeBehind) {
        $cs = Get-Content $codeBehind -Raw
        foreach ($handler in $handlers) {
            if ($cs -notmatch "\b$([regex]::Escape($handler))\s*\(") { Write-Host "FAIL missing handler '$handler' in $([System.IO.Path]::GetFileName($codeBehind))"; $failed = $true }
        }
    }
}

if ($failed) { Write-Host 'Stage 10 static prep: FAILED'; exit 1 }
Write-Host 'Stage 10 static prep: PASSED'
Write-Host 'Note: this does not replace a real-device test or dotnet build.'
