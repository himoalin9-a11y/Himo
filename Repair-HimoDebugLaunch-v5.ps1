[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$projectPath = Join-Path $ProjectRoot 'Himo.csproj'
$userPath = Join-Path $ProjectRoot 'Himo.csproj.user'
$activityPath = Join-Path $ProjectRoot 'Platforms\Android\MainActivity.cs'
$manifestPath = Join-Path $ProjectRoot 'Platforms\Android\AndroidManifest.xml'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'

foreach ($required in @($projectPath, $activityPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required project file is missing: $required. No files were changed."
    }
}
if (Get-Process -Name devenv -ErrorAction SilentlyContinue) {
    throw 'Close Visual Studio completely before running this repair. No files were changed.'
}

$projectText = [System.IO.File]::ReadAllText($projectPath)
$activityText = [System.IO.File]::ReadAllText($activityPath)
$userText = if (Test-Path -LiteralPath $userPath) { [System.IO.File]::ReadAllText($userPath) } else { $null }
$appIdMatch = [regex]::Match($projectText, '(?s)<ApplicationId>\s*(?<value>[^<]+?)\s*</ApplicationId>')
$appId = if ($appIdMatch.Success) { $appIdMatch.Groups['value'].Value.Trim() } else { 'com.companyname.himo' }

# Read the actual Java/Android component name from the Activity attribute first.
$attributeMatch = [regex]::Match($activityText, '(?s)\[(?:global::)?Android\.App\.Activity\((?<args>.*?)\)\]')
$activityAttributeArgs = if ($attributeMatch.Success) { $attributeMatch.Groups['args'].Value } else { '' }
$nameMatch = [regex]::Match($activityAttributeArgs, '\bName\s*=\s*"(?<name>[^"]+)"')
$componentName = if ($nameMatch.Success) { $nameMatch.Groups['name'].Value.Trim() } else { $null }
$sourceOfName = if ($componentName) { 'MainActivity.cs Activity(Name=...)' } else { $null }

# If the C# attribute has no explicit Name, inspect the source Android manifest and
# use the concrete activity component (not an activity-alias display icon).
if ([string]::IsNullOrWhiteSpace($componentName) -and (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    try {
        $manifest = [System.Xml.XmlDocument]::new()
        $manifest.XmlResolver = $null
        $manifest.Load($manifestPath)
        $androidNs = 'http://schemas.android.com/apk/res/android'
        $concrete = @($manifest.SelectNodes('/manifest/application/activity')) |
            Where-Object { $_.GetAttribute('name', $androidNs) } |
            Select-Object -First 1
        if ($concrete) {
            $componentName = $concrete.GetAttribute('name', $androidNs)
            $sourceOfName = 'Platforms\Android\AndroidManifest.xml activity'
        }
    } catch {
        Write-Warning "Could not parse AndroidManifest.xml; continuing with C# / project settings. $($_.Exception.Message)"
    }
}

# Normalize a short or relative name into Android's fully qualified component name.
if ([string]::IsNullOrWhiteSpace($componentName)) {
    throw @"
Could not determine the registered Android Activity name from MainActivity.cs or AndroidManifest.xml.
No files were changed. Run this diagnostic and paste its output:
  Select-String -Path '$activityPath' -Pattern 'Activity\(|Name\s*=|MainLauncher|class MainActivity' -Context 2,4
"@
}
if ($componentName.StartsWith('.')) {
    $componentName = $appId + $componentName
} elseif (-not $componentName.Contains('.')) {
    $componentName = $appId + '.' + $componentName
}

# Make Debug's Activity a valid launch target. Keep Release behavior unchanged.
$newActivityText = $activityText
$activityAttrNow = [regex]::Match($newActivityText, '(?s)\[(?:global::)?Android\.App\.Activity\((?<args>.*?)\)\]')
if (-not $activityAttrNow.Success) {
    throw 'Could not safely locate the Android Activity attribute in MainActivity.cs. No files were changed.'
}
$args = $activityAttrNow.Groups['args'].Value
$debugLauncherPattern = '(?s)#if\s+DEBUG\s*\r?\n\s*MainLauncher\s*=\s*true\s*,\s*#else\s*\r?\n\s*MainLauncher\s*=\s*false\s*,\s*#endif'
if (-not [regex]::IsMatch($args, $debugLauncherPattern)) {
    $mainLauncherMatch = [regex]::Match($args, '(?m)^(?<indent>[ \t]*)MainLauncher[ \t]*=[ \t]*(?<value>true|false)[ \t]*,[ \t]*$')
    if ($mainLauncherMatch.Success) {
        $indent = $mainLauncherMatch.Groups['indent'].Value
        $replacement = "#if DEBUG`r`n${indent}MainLauncher = true,`r`n#else`r`n${indent}MainLauncher = false,`r`n#endif"
        if ($mainLauncherMatch.Groups['value'].Value -eq 'true') {
            # An unconditional true already launches in Debug; only normalize if a false value was found.
            $replacement = $mainLauncherMatch.Value
        }
        $args = $args.Remove($mainLauncherMatch.Index, $mainLauncherMatch.Length).Insert($mainLauncherMatch.Index, $replacement)
    } elseif ($args -match '(?m)^[ \t]*Exported[ \t]*=[ \t]*true[ \t]*,') {
        $exportMatch = [regex]::Match($args, '(?m)^(?<indent>[ \t]*)Exported[ \t]*=[ \t]*true[ \t]*,')
        $indent = $exportMatch.Groups['indent'].Value
        $insert = "#if DEBUG`r`n${indent}MainLauncher = true,`r`n#else`r`n${indent}MainLauncher = false,`r`n#endif`r`n${indent}"
        $args = $args.Insert($exportMatch.Index, $insert)
    } else {
        throw 'Could not safely set MainLauncher in the Activity attribute. No files were changed.'
    }
    $newActivityText = $newActivityText.Remove($activityAttrNow.Groups['args'].Index, $activityAttrNow.Groups['args'].Length).Insert($activityAttrNow.Groups['args'].Index, $args)
}

# Align all project launch properties to the actual registered Android Activity name.
$newProjectText = [regex]::Replace(
    $projectText,
    '(?s)<AndroidLaunchActivity>\s*[^<]*\s*</AndroidLaunchActivity>',
    '<AndroidLaunchActivity>' + [System.Security.SecurityElement]::Escape($componentName) + '</AndroidLaunchActivity>'
)
if ($newProjectText -ceq $projectText -and $projectText -notmatch '<AndroidLaunchActivity>') {
    $debugGroup = [regex]::Match($projectText, '(?s)<PropertyGroup\s+Condition="[^"]*\$\(Configuration\)[^"]*Debug[^"]*"[^>]*>.*?</PropertyGroup>')
    if (-not $debugGroup.Success) {
        throw 'Could not find the Debug PropertyGroup to add AndroidLaunchActivity. No files were changed.'
    }
    $insertAt = $debugGroup.Index + $debugGroup.Length - '</PropertyGroup>'.Length
    $newProjectText = $projectText.Insert($insertAt, "    <AndroidLaunchActivity>$([System.Security.SecurityElement]::Escape($componentName))</AndroidLaunchActivity>`r`n  ")
}

# Remove the earlier hard-coded manifest post-processor hook if present. It assumes a
# particular icon alias name and can undo the corrected launch target in another version.
$newProjectText = [regex]::Replace($newProjectText, '(?s)\s*<Target\s+Name="HimoStripDebugLauncherAliases"[^>]*>.*?</Target>', '')
$newProjectText = [regex]::Replace(
    $newProjectText,
    '(?m)^[ \t]*<AfterGenerateAndroidManifest>(?<value>.*?)</AfterGenerateAndroidManifest>[ \t]*\r?\n',
    [System.Text.RegularExpressions.MatchEvaluator]{
        param($m)
        $value = $m.Groups['value'].Value
        $value = [regex]::Replace($value, ';?\s*HimoStripDebugLauncherAliases\s*', '')
        $value = $value.Trim().Trim(';')
        if ([string]::IsNullOrWhiteSpace($value) -or $value -eq '$(AfterGenerateAndroidManifest)') { return '' }
        return "    <AfterGenerateAndroidManifest>$value</AfterGenerateAndroidManifest>`r`n"
    }
)

$newUserText = $userText
if ($null -ne $userText) {
    $escapedComponentName = [System.Security.SecurityElement]::Escape($componentName)
    if ([regex]::IsMatch($userText, '(?s)<AndroidLaunchActivity>\s*[^<]*\s*</AndroidLaunchActivity>')) {
        $newUserText = [regex]::Replace($userText, '(?s)<AndroidLaunchActivity>\s*[^<]*\s*</AndroidLaunchActivity>', '<AndroidLaunchActivity>' + $escapedComponentName + '</AndroidLaunchActivity>')
    } else {
        $groupClose = $userText.IndexOf('</PropertyGroup>', [System.StringComparison]::OrdinalIgnoreCase)
        if ($groupClose -ge 0) {
            $newUserText = $userText.Insert($groupClose, "    <AndroidLaunchActivity>$escapedComponentName</AndroidLaunchActivity>`r`n  ")
        } else {
            $projectClose = $userText.LastIndexOf('</Project>', [System.StringComparison]::OrdinalIgnoreCase)
            if ($projectClose -ge 0) {
                $newUserText = $userText.Insert($projectClose, "  <PropertyGroup>`r`n    <AndroidLaunchActivity>$escapedComponentName</AndroidLaunchActivity>`r`n  </PropertyGroup>`r`n")
            } else {
                throw 'Himo.csproj.user is not a valid MSBuild XML project. No files were changed.'
            }
        }
    }
}

# Validate XML project files before touching any on-disk files.
$projectXmlCheck = [System.Xml.XmlDocument]::new(); $projectXmlCheck.XmlResolver = $null; $projectXmlCheck.LoadXml($newProjectText)
if ($null -ne $newUserText) { $userXmlCheck = [System.Xml.XmlDocument]::new(); $userXmlCheck.XmlResolver = $null; $userXmlCheck.LoadXml($newUserText) }

$changes = @()
if ($newActivityText -cne $activityText) { $changes += $activityPath }
if ($newProjectText -cne $projectText) { $changes += $projectPath }
if ($null -ne $newUserText -and $newUserText -cne $userText) { $changes += $userPath }

if ($changes.Count -eq 0) {
    Write-Host 'No text changes were needed; settings already match the registered activity.' -ForegroundColor Yellow
} else {
    foreach ($path in $changes) {
        $backupPath = "$path.backup-launch-v5-$timestamp"
        Copy-Item -LiteralPath $path -Destination $backupPath -Force
    }
    if ($newActivityText -cne $activityText) { [System.IO.File]::WriteAllText($activityPath, $newActivityText, $utf8NoBom) }
    if ($newProjectText -cne $projectText) { [System.IO.File]::WriteAllText($projectPath, $newProjectText, $utf8NoBom) }
    if ($null -ne $newUserText -and $newUserText -cne $userText) { [System.IO.File]::WriteAllText($userPath, $newUserText, $utf8NoBom) }
}

# Clear only Debug Android outputs so stale manifests cannot override the repaired source.
foreach ($relative in @('obj\Debug\net10.0-android36.0', 'bin\Debug\net10.0-android36.0')) {
    $path = Join-Path $ProjectRoot $relative
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "Removed stale Debug output: $path" -ForegroundColor DarkCyan
    }
}

Write-Host ''
Write-Host 'Himo Debug launch settings repaired.' -ForegroundColor Green
Write-Host "Actual Android Activity: $componentName" -ForegroundColor Cyan
Write-Host "Detected from: $sourceOfName"
Write-Host 'Updated: MainActivity Debug launch flag, Himo.csproj AndroidLaunchActivity, and Himo.csproj.user when present.'
Write-Host 'Removed the obsolete hard-coded HimoStripDebugLauncherAliases post-processing target when present.'
Write-Host 'Backups were created for every changed source/settings file.'
Write-Host ''
Write-Host 'Next: reopen Himo.sln, select Debug + net10.0-android36.0 + your HONOR device, then Rebuild Solution and press F5.'
