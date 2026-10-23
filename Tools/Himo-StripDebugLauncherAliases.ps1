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
