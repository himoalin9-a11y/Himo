[CmdletBinding()]
param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    throw 'Pass -ProjectRoot with the folder containing Himo.csproj.'
}
$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$projectPath = Join-Path $ProjectRoot 'Himo.csproj'
$toolsDirectory = Join-Path $ProjectRoot 'Tools'
$helperPath = Join-Path $toolsDirectory 'Himo-StripDebugLauncherAliases.ps1'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Himo.csproj not found: $projectPath"
}
if (Get-Process -Name devenv -ErrorAction SilentlyContinue) {
    throw 'Close Visual Studio completely before running this fix.'
}

$projectText = [System.IO.File]::ReadAllText($projectPath)
if ($projectText -notmatch 'HimoStripDebugLauncherAliases') {
    throw 'Himo.csproj does not contain the Debug manifest hook from v3. No files were changed.'
}
if (-not (Test-Path -LiteralPath $toolsDirectory)) {
    [void](New-Item -ItemType Directory -Path $toolsDirectory -Force)
}

$helperText = @'
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$androidNs = 'http://schemas.android.com/apk/res/android'

try {
    $ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Generated Android manifest was not found: $ManifestPath"
    }

    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $false
    $doc.XmlResolver = $null
    $doc.Load($ManifestPath)
    $ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable)
    $ns.AddNamespace('android', $androidNs)

    $application = $doc.SelectSingleNode('/manifest/application')
    if ($null -eq $application) {
        throw "No /manifest/application node in generated manifest: $ManifestPath"
    }

    $aliases = @($doc.SelectNodes('/manifest/application/activity-alias'))
    $removedFilters = 0
    foreach ($alias in $aliases) {
        foreach ($filter in @($alias.SelectNodes('intent-filter'))) {
            $main = $false
            $launcher = $false
            foreach ($action in @($filter.SelectNodes('action'))) {
                if ($action.GetAttribute('name', $androidNs) -eq 'android.intent.action.MAIN') { $main = $true }
            }
            foreach ($category in @($filter.SelectNodes('category'))) {
                if ($category.GetAttribute('name', $androidNs) -eq 'android.intent.category.LAUNCHER') { $launcher = $true }
            }
            if ($main -and $launcher) {
                [void]$alias.RemoveChild($filter)
                $removedFilters++
            }
        }
    }

    $stable = $doc.SelectSingleNode("/manifest/application/activity[@android:name='com.companyname.himo.LauncherIconDefault']", $ns)
    if ($null -eq $stable) {
        $declared = @($doc.SelectNodes('/manifest/application/activity')) | ForEach-Object {
            $_.GetAttribute('name', $androidNs)
        }
        throw "Stable activity com.companyname.himo.LauncherIconDefault was not found. Declared activities: $($declared -join ', ')"
    }

    # Add the launcher filter to the stable activity if manifest merging did not carry it over.
    $stableHasLauncher = $false
    foreach ($filter in @($stable.SelectNodes('intent-filter'))) {
        $main = $false
        $launcher = $false
        foreach ($action in @($filter.SelectNodes('action'))) {
            if ($action.GetAttribute('name', $androidNs) -eq 'android.intent.action.MAIN') { $main = $true }
        }
        foreach ($category in @($filter.SelectNodes('category'))) {
            if ($category.GetAttribute('name', $androidNs) -eq 'android.intent.category.LAUNCHER') { $launcher = $true }
        }
        if ($main -and $launcher) { $stableHasLauncher = $true; break }
    }

    if (-not $stableHasLauncher) {
        $filter = $doc.CreateElement('intent-filter')
        $action = $doc.CreateElement('action')
        $actionName = $doc.CreateAttribute('android', 'name', $androidNs)
        $actionName.Value = 'android.intent.action.MAIN'
        [void]$action.Attributes.Append($actionName)
        [void]$filter.AppendChild($action)

        $category = $doc.CreateElement('category')
        $categoryName = $doc.CreateAttribute('android', 'name', $androidNs)
        $categoryName.Value = 'android.intent.category.LAUNCHER'
        [void]$category.Attributes.Append($categoryName)
        [void]$filter.AppendChild($category)
        [void]$stable.AppendChild($filter)
        $stableHasLauncher = $true
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.IndentChars = '  '
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $writer = [System.Xml.XmlWriter]::Create($ManifestPath, $settings)
    try {
        $doc.Save($writer)
    }
    finally {
        $writer.Dispose()
    }

    Write-Host "Debug manifest adjusted: $ManifestPath"
    Write-Host "Removed MAIN/LAUNCHER filters from activity aliases: $removedFilters"
    Write-Host "Stable activity MAIN/LAUNCHER filter present: $stableHasLauncher"
    Write-Host 'Verified stable launch activity: com.companyname.himo.LauncherIconDefault'
}
catch {
    Write-Error ("Himo Debug manifest helper failed: " + $_.Exception.ToString())
    exit 1
}
'@

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
if (Test-Path -LiteralPath $helperPath) {
    $backupPath = "$helperPath.backup-v4-$timestamp"
    [System.IO.File]::Copy($helperPath, $backupPath, $false)
    Write-Host "Backup: $backupPath"
}
[System.IO.File]::WriteAllText($helperPath, $helperText, $utf8NoBom)

# Clear intermediates so the next build is regenerated from the corrected helper.
foreach ($name in @('bin', 'obj')) {
    $path = Join-Path $ProjectRoot $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "Removed stale build output: $path"
    }
}

# Verify that the project remains valid XML and the referenced helper exists.
$xml = [System.Xml.XmlDocument]::new()
$xml.XmlResolver = $null
$xml.Load($projectPath)
if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "Helper installation failed: $helperPath"
}

Write-Host ''
Write-Host 'Updated the Debug manifest helper.' -ForegroundColor Green
Write-Host 'The helper now removes launcher filters from aliases and adds MAIN/LAUNCHER to LauncherIconDefault when missing.'
Write-Host 'Only generated Debug manifest output is edited; the source AndroidManifest.xml and Release manifest behavior are unchanged.'
Write-Host 'Next: reopen Himo.sln and run Rebuild Solution, or run the sequential dotnet build command.'
