[CmdletBinding()]
param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { throw 'Pass -ProjectRoot with the folder containing Himo.csproj.' }
$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$projectPath = Join-Path $ProjectRoot 'Himo.csproj'
$activityPath = Join-Path $ProjectRoot 'Platforms\Android\MainActivity.cs'
$toolsDirectory = Join-Path $ProjectRoot 'Tools'
$helperPath = Join-Path $toolsDirectory 'Himo-StripDebugLauncherAliases.ps1'
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

foreach ($required in @($projectPath, $activityPath)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required project file not found: $required" }
}
if (Get-Process -Name devenv -ErrorAction SilentlyContinue) {
    throw 'Close Visual Studio completely before running this fix, then try again.'
}

$projectText = [System.IO.File]::ReadAllText($projectPath)
$activityText = [System.IO.File]::ReadAllText($activityPath)

# Identify the Debug PropertyGroup which owns AndroidLaunchActivity.
$debugGroups = [System.Text.RegularExpressions.Regex]::Matches(
    $projectText,
    '(?s)<PropertyGroup\b(?=[^>]*Condition="[^"]*Debug[^"]*")[^>]*>.*?</PropertyGroup>'
)
$debugGroup = $null
foreach ($group in $debugGroups) {
    if ($group.Value -match '<AndroidLaunchActivity>') { $debugGroup = $group; break }
}
if ($null -eq $debugGroup) {
    throw 'Could not find the Debug PropertyGroup containing AndroidLaunchActivity in Himo.csproj. No files were changed.'
}

$newDebugGroup = $debugGroup.Value
# The MAUI SingleProject targets reassign AndroidManifest to Platforms\Android\AndroidManifest.xml.
# Do not keep the ineffective Debug-only AndroidManifest value; patch the generated manifest instead.
$newDebugGroup = [System.Text.RegularExpressions.Regex]::Replace(
    $newDebugGroup,
    '(?m)^[ \t]*<AndroidManifest>[ \t]*Platforms[\\/]Android[\\/]AndroidManifest\.Debug\.xml[ \t]*</AndroidManifest>[ \t]*\r?\n',
    ''
)

$hookPattern = '(?s)(?<open><AfterGenerateAndroidManifest(?:\s+[^>]*)?>)(?<value>.*?)(?<close></AfterGenerateAndroidManifest>)'
$hookMatch = [System.Text.RegularExpressions.Regex]::Match($newDebugGroup, $hookPattern)
if ($hookMatch.Success) {
    $hookValue = $hookMatch.Groups['value'].Value.Trim()
    if ($hookValue -notmatch '\bHimoStripDebugLauncherAliases\b') {
        if ([string]::IsNullOrWhiteSpace($hookValue)) { $hookValue = 'HimoStripDebugLauncherAliases' }
        else { $hookValue = $hookValue.TrimEnd(';') + ';HimoStripDebugLauncherAliases' }
        $replacement = $hookMatch.Groups['open'].Value + $hookValue + $hookMatch.Groups['close'].Value
        $newDebugGroup = $newDebugGroup.Remove($hookMatch.Index, $hookMatch.Length).Insert($hookMatch.Index, $replacement)
    }
} else {
    $groupEnd = [System.Text.RegularExpressions.Regex]::Match($newDebugGroup, '(?m)^[ \t]*</PropertyGroup>[ \t]*$')
    if (-not $groupEnd.Success) { throw 'Could not find the closing tag for the Debug PropertyGroup. No files were changed.' }
    $insertedHook = "    <AfterGenerateAndroidManifest>`$(AfterGenerateAndroidManifest);HimoStripDebugLauncherAliases</AfterGenerateAndroidManifest>`r`n  "
    $newDebugGroup = $newDebugGroup.Substring(0, $groupEnd.Index) + $insertedHook + '</PropertyGroup>'
}

$newProjectText = $projectText.Substring(0, $debugGroup.Index) + $newDebugGroup + $projectText.Substring($debugGroup.Index + $debugGroup.Length)

# Install a target which runs at .NET for Android's documented AfterGenerateAndroidManifest extension point.
if ($newProjectText -notmatch '<Target\s+Name="HimoStripDebugLauncherAliases"') {
    $targetXml = @'

  <!-- For Debug only, remove launcher intent filters from icon aliases after MAUI merges the manifest. -->
  <Target Name="HimoStripDebugLauncherAliases" Condition=" '$(Configuration)' == 'Debug' and '$(TargetFramework)' == 'net10.0-android36.0' and '$(DesignTimeBuild)' != 'true' ">
    <Exec Command="powershell.exe -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildProjectDirectory)\Tools\Himo-StripDebugLauncherAliases.ps1&quot; -ManifestPath &quot;$(MSBuildProjectDirectory)\$(IntermediateOutputPath)android\AndroidManifest.xml&quot;" />
  </Target>
'@
    $projectEnd = [System.Text.RegularExpressions.Regex]::Match($newProjectText, '</Project>\s*$')
    if (-not $projectEnd.Success) { throw 'Himo.csproj does not end with </Project>. No files were changed.' }
    $newProjectText = $newProjectText.Substring(0, $projectEnd.Index) + $targetXml + "`r`n</Project>`r`n"
}

# Guarantee the stable activity is launchable in Debug only, without changing Release.
$conditionalPattern = '(?s)#if\s+DEBUG\s*\r?\n\s*MainLauncher\s*=\s*true\s*,\s*#else\s*\r?\n\s*MainLauncher\s*=\s*false\s*,\s*#endif'
$activityChanged = $false
if (-not [System.Text.RegularExpressions.Regex]::IsMatch($activityText, $conditionalPattern)) {
    $mainLauncherPattern = '(?m)^(?<indent>[ \t]*)MainLauncher\s*=\s*false\s*,[ \t]*$'
    $mainLauncherMatches = [System.Text.RegularExpressions.Regex]::Matches($activityText, $mainLauncherPattern)
    if ($mainLauncherMatches.Count -ne 1) {
        throw "Expected one MainLauncher = false or an existing Debug conditional in MainActivity.cs; found $($mainLauncherMatches.Count). No files were changed."
    }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $indent = $match.Groups['indent'].Value
        return "#if DEBUG`r`n${indent}MainLauncher = true,`r`n#else`r`n${indent}MainLauncher = false,`r`n#endif"
    }
    $activityText = [System.Text.RegularExpressions.Regex]::new($mainLauncherPattern).Replace($activityText, $evaluator, 1)
    $activityChanged = $true
}

$helperText = @'
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Generated Android manifest was not found: $ManifestPath"
}

$androidNs = 'http://schemas.android.com/apk/res/android'
$doc = [System.Xml.XmlDocument]::new()
$doc.PreserveWhitespace = $false
$doc.XmlResolver = $null
$doc.Load($ManifestPath)
$ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable)
$ns.AddNamespace('android', $androidNs)

$aliases = @($doc.SelectNodes('/manifest/application/activity-alias'))
if ($aliases.Count -eq 0) {
    throw "No activity-alias elements found in generated manifest: $ManifestPath"
}

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

# Fail the Debug build instead of producing an app without a stable launch entry.
$stable = $doc.SelectSingleNode("/manifest/application/activity[@android:name='com.companyname.himo.LauncherIconDefault']", $ns)
if ($null -eq $stable) {
    throw 'Stable activity com.companyname.himo.LauncherIconDefault was not found in the generated manifest.'
}
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
    if ($main -and $launcher) { $stableHasLauncher = $true }
}
if (-not $stableHasLauncher) {
    throw 'LauncherIconDefault does not have a MAIN/LAUNCHER filter. Confirm MainActivity.cs sets MainLauncher=true under #if DEBUG.'
}

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$settings.IndentChars = '  '
$settings.NewLineChars = "`r`n"
$settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
$writer = [System.Xml.XmlWriter]::Create($ManifestPath, $settings)
try { $doc.Save($writer) } finally { $writer.Dispose() }

Write-Host "Debug manifest adjusted: $ManifestPath"
Write-Host "Removed MAIN/LAUNCHER filters from activity aliases: $removedFilters"
Write-Host 'Verified stable launch activity: com.companyname.himo.LauncherIconDefault'
'@

# Prepare all output and backups before touching project files.
if (-not (Test-Path -LiteralPath $toolsDirectory)) { [void](New-Item -ItemType Directory -Path $toolsDirectory -Force) }
$backups = @()
if (-not [System.String]::Equals($projectText, $newProjectText, [System.StringComparison]::Ordinal)) {
    $backup = "$projectPath.backup-debug-manifest-v3-$timestamp"
    [System.IO.File]::Copy($projectPath, $backup, $false)
    $backups += $backup
}
if ($activityChanged) {
    $backup = "$activityPath.backup-debug-manifest-v3-$timestamp"
    [System.IO.File]::Copy($activityPath, $backup, $false)
    $backups += $backup
}
if (Test-Path -LiteralPath $helperPath) {
    $backup = "$helperPath.backup-debug-manifest-v3-$timestamp"
    [System.IO.File]::Copy($helperPath, $backup, $false)
    $backups += $backup
}

[System.IO.File]::WriteAllText($projectPath, $newProjectText, $utf8NoBom)
if ($activityChanged) { [System.IO.File]::WriteAllText($activityPath, $activityText, $utf8NoBom) }
[System.IO.File]::WriteAllText($helperPath, $helperText, $utf8NoBom)

foreach ($relative in @('bin', 'obj')) {
    $path = Join-Path $ProjectRoot $relative
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "Removed stale build output: $path" -ForegroundColor DarkCyan
    }
}

# Ensure the edited project remains well-formed XML.
$verify = [System.Xml.XmlDocument]::new()
$verify.XmlResolver = $null
$verify.Load($projectPath)

Write-Host ''
Write-Host 'Applied Debug manifest post-generation fix.' -ForegroundColor Green
Write-Host 'The MAUI SDK may still select Platforms\Android\AndroidManifest.xml as its source template; the Debug target now adjusts the generated manifest after merging.'
Write-Host 'Release is unchanged: the target only runs for Configuration=Debug and net10.0-android36.0.'
Write-Host "Helper script: $helperPath"
if ($backups.Count -gt 0) {
    Write-Host 'Backups:'
    $backups | ForEach-Object { Write-Host "  $_" }
}
Write-Host 'Next: reopen Himo.sln, Rebuild Solution, and confirm the helper output says LauncherIconDefault was verified.'
Write-Host 'The official AfterGenerateAndroidManifest extension operates on the manifest under obj\Debug\net10.0-android36.0\android\AndroidManifest.xml; HomeDefault should no longer have MAIN/LAUNCHER there, while LauncherIconDefault should have it.'
