$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "Himo Stage 12 static integrity check"

[xml]$app = Get-Content -Raw (Join-Path $root 'App.xaml')
$xamlFiles = Get-ChildItem -Recurse -Filter *.xaml | Where-Object { $_.FullName -notmatch '\\.git\\' }
foreach ($file in $xamlFiles) {
    try { [xml](Get-Content -Raw $file.FullName) | Out-Null }
    catch { throw "Invalid XAML/XML: $($file.FullName) :: $($_.Exception.Message)" }
}

$ns = @{ x = 'http://schemas.microsoft.com/winfx/2009/xaml' }
$resourceKeys = @{}
foreach ($node in $app.Application.Resources.ResourceDictionary.ChildNodes) {
    if ($node.Attributes -and $node.Attributes['x:Key']) {
        $key = $node.Attributes['x:Key'].Value
        if ($resourceKeys.ContainsKey($key)) { throw "Duplicate resource key: $key" }
        $resourceKeys[$key] = $true
    }
}

foreach ($file in $xamlFiles) {
    $xml = [xml](Get-Content -Raw $file.FullName)
    $names = @($xml.SelectNodes('//*[@x:Name]', $ns) | ForEach-Object { $_.GetAttribute('x:Name') })
    $dupes = $names | Group-Object | Where-Object Count -gt 1
    if ($dupes) { throw "Duplicate x:Name in $($file.FullName): $($dupes.Name -join ', ')" }

    $codeBehind = [IO.Path]::ChangeExtension($file.FullName, '.xaml.cs')
    if (-not (Test-Path $codeBehind)) { continue }
    $code = Get-Content -Raw $codeBehind
    foreach ($attr in @('Clicked','Tapped','Toggled','TextChanged','SearchButtonPressed','Completed','SelectionChanged','Refreshing','Appearing','Disappearing')) {
        foreach ($handler in ($xml.SelectNodes("//*[@$attr]", $ns) | ForEach-Object { $_.GetAttribute($attr) } | Where-Object { $_ })) {
            if ($code -notmatch "\b$([regex]::Escape($handler))\s*\(") {
                throw "Missing handler '$handler' referenced by $($file.FullName)"
            }
        }
    }
}

Write-Host "Static integrity check passed: $($xamlFiles.Count) XAML files checked."
