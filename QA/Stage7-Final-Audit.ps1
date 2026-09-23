$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$issues = New-Object System.Collections.Generic.List[string]

foreach ($xaml in Get-ChildItem (Join-Path $root 'Views') -Filter *.xaml) {
    $x = Get-Content $xaml.FullName -Raw
    $csPath = [System.IO.Path]::ChangeExtension($xaml.FullName, '.xaml.cs')
    $cs = if (Test-Path $csPath) { Get-Content $csPath -Raw } else { '' }
    foreach ($m in [regex]::Matches($x, '(?:Clicked|Toggled|TextChanged|Completed|SelectionChanged|Refreshing)="([^"]+)"')) {
        $handler = $m.Groups[1].Value
        if ($cs -notmatch ('\b' + [regex]::Escape($handler) + '\s*\(')) { $issues.Add("Missing handler: $($xaml.Name) -> $handler") }
    }
    $names = @([regex]::Matches($x, 'x:Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    foreach ($n in ($names | Group-Object | Where-Object Count -gt 1)) { $issues.Add("Duplicate x:Name: $($xaml.Name) -> $($n.Name)") }
    foreach ($dt in [regex]::Matches($x, '<DataTemplate(?<attrs>[^>]*)>')) {
        if ($dt.Groups['attrs'].Value -notmatch 'x:DataType=') { $issues.Add("DataTemplate without x:DataType: $($xaml.Name)") }
    }
}

$allSource = (Get-ChildItem $root -Recurse -File -Include *.xaml,*.cs | Get-Content -Raw) -join "`n"
foreach ($obsolete in @('ApiUrlEntry','ApiStatusLabel','TestApiButton')) {
    if ($allSource -match ('\b' + $obsolete + '\b')) { $issues.Add("Obsolete server UI reference remains: $obsolete") }
}
if (Test-Path (Join-Path $root 'bin')) { $issues.Add('bin directory must not be packaged') }
if (Test-Path (Join-Path $root 'obj')) { $issues.Add('obj directory must not be packaged') }
if (Test-Path (Join-Path $root '.vs')) { $issues.Add('.vs directory must not be packaged') }

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}
Write-Host 'Himo Stage 7 static audit: PASS'
Write-Host 'Checked XAML event handlers, duplicate x:Name values, compiled DataTemplates, removed server UI references, and packaged build-cache directories.'
