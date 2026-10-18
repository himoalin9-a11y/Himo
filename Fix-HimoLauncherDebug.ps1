$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$projectPath = Join-Path $root 'Himo.csproj'
$userPath = Join-Path $root 'Himo.csproj.user'
$stableActivity = 'com.companyname.himo.LauncherIconDefault'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Himo.csproj was not found in $root. Extract this script beside Himo.csproj."
}

if (Get-Process -Name devenv -ErrorAction SilentlyContinue) {
    throw 'Close every Visual Studio window before running this script, then try again.'
}

function Set-AndroidLaunchActivity {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $tag = '<AndroidLaunchActivity>' + $Value + '</AndroidLaunchActivity>'
    $pattern = '<AndroidLaunchActivity>[^<]*</AndroidLaunchActivity>'
    if ([System.Text.RegularExpressions.Regex]::IsMatch($Text, $pattern)) {
        return [System.Text.RegularExpressions.Regex]::Replace($Text, $pattern, $tag)
    }

    # User-specific settings are imported by the IDE/MSBuild separately; add the property
    # there even when the tag did not exist previously.
    $groupClose = $Text.IndexOf('</PropertyGroup>', [System.StringComparison]::OrdinalIgnoreCase)
    if ($groupClose -ge 0) {
        return $Text.Insert($groupClose, "  $tag`r`n  ")
    }

    $projectClose = $Text.LastIndexOf('</Project>', [System.StringComparison]::OrdinalIgnoreCase)
    if ($projectClose -ge 0) {
        return $Text.Insert($projectClose, "  <PropertyGroup>`r`n    $tag`r`n  </PropertyGroup>`r`n")
    }

    throw 'Could not find a valid PropertyGroup or Project closing tag while setting AndroidLaunchActivity.'
}

function Save-TextWithBackup {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewText
    )

    $oldText = [System.IO.File]::ReadAllText($Path)
    if ($oldText -ceq $NewText) { return $false }

    $backupPath = $Path + '.backup-before-launcher-repair'
    if (-not (Test-Path -LiteralPath $backupPath)) {
        [System.IO.File]::WriteAllText($backupPath, $oldText, $utf8NoBom)
    }
    [System.IO.File]::WriteAllText($Path, $NewText, $utf8NoBom)
    Write-Host ("Updated: " + [System.IO.Path]::GetFileName($Path)) -ForegroundColor Green
    return $true
}

# Confirm that the stable component exists in both the manifest and the C# Activity.
$manifestPath = Join-Path $root 'Platforms\Android\AndroidManifest.xml'
$activityPath = Join-Path $root 'Platforms\Android\MainActivity.cs'
if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $activityPath)) {
    throw 'The Android manifest or MainActivity.cs is missing. No files were changed.'
}
$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$activityText = [System.IO.File]::ReadAllText($activityPath)
if ($manifestText -notmatch [regex]::Escape($stableActivity) -or $activityText -notmatch [regex]::Escape($stableActivity)) {
    throw 'The project does not define LauncherIconDefault as expected. No launch settings were changed.'
}

$projectText = [System.IO.File]::ReadAllText($projectPath)
$newProjectText = Set-AndroidLaunchActivity -Text $projectText -Value $stableActivity
# Remove the old build target that called `adb shell pm enable`; some devices deny this
# command with SecurityException, and ignoring that failure does not repair the debugger target.
$obsoleteTargetPattern = '(?s)<Target\s+Name="HimoEnableStableDebugActivity"[^>]*>.*?</Target>'
$newProjectText = [System.Text.RegularExpressions.Regex]::Replace($newProjectText, $obsoleteTargetPattern, '')
[void](Save-TextWithBackup -Path $projectPath -NewText $newProjectText)

if (Test-Path -LiteralPath $userPath) {
    $userText = [System.IO.File]::ReadAllText($userPath)
    $newUserText = Set-AndroidLaunchActivity -Text $userText -Value $stableActivity
    [void](Save-TextWithBackup -Path $userPath -NewText $newUserText)
}
else {
    $userText = @"
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <AndroidLaunchActivity>$stableActivity</AndroidLaunchActivity>
  </PropertyGroup>
</Project>
"@
    [System.IO.File]::WriteAllText($userPath, $userText, $utf8NoBom)
    Write-Host 'Created Himo.csproj.user with an explicit stable launch activity.' -ForegroundColor Green
}

# Remove stale launch/build caches so Visual Studio re-evaluates the corrected activity.
foreach ($relativePath in @('.vs', 'bin', 'obj')) {
    $path = Join-Path $root $relativePath
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host ("Removed stale cache/output: " + $path) -ForegroundColor DarkCyan
    }
}

Write-Host ''
Write-Host 'Launcher settings repaired.' -ForegroundColor Green
Write-Host "Visual Studio should launch $stableActivity, not LauncherIconHomeDefault."
Write-Host 'Open Himo.sln, select Himo as Startup Project, then Rebuild and Deploy.'
Write-Host 'This script does not run adb pm enable and does not uninstall Himo or erase app data.'
