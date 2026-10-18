param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$PatchRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $MyInvocation.MyCommand.Path)).Path
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

$requiredProjectFiles = @(
    (Join-Path $ProjectRoot 'Himo.csproj'),
    (Join-Path $ProjectRoot 'Himo.Api\Himo.Api.csproj')
)
foreach ($projectFile in $requiredProjectFiles) {
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "Project file not found: $projectFile"
    }
}

$relativeFiles = @(
    'Himo.Api\FcmPushService.cs',
    'Himo.Api\Program.cs',
    'MauiProgram.cs',
    'Platforms\Android\AndroidManifest.xml',
    'Platforms\Android\MainActivity.cs',
    'Platforms\Android\MainApplication.cs',
    'Platforms\Android\Services\AndroidWebRtcMediaEngine.cs',
    'Platforms\Android\Services\NotificationService.cs',
    'Services\CallService.cs',
    'Services\HimoRealtimeService.cs',
    'Services\INotificationService.cs',
    'Services\PushNotificationManager.cs',
    'Views\CallPage.xaml.cs'
)

foreach ($relativePath in $relativeFiles) {
    $patchFile = Join-Path $PatchRoot $relativePath
    $destination = Join-Path $ProjectRoot $relativePath
    if (-not (Test-Path -LiteralPath $patchFile -PathType Leaf)) {
        throw "Patch file is missing: $patchFile"
    }
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        throw "Target file is missing; no changes have been made: $destination"
    }
}

$backupParent = Split-Path -Parent $ProjectRoot
$backupDir = Join-Path $backupParent ('.HimoCallBackup_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

try {
    foreach ($relativePath in $relativeFiles) {
        $source = Join-Path $ProjectRoot $relativePath
        $backup = Join-Path $backupDir $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $backup -Force
    }

    foreach ($relativePath in $relativeFiles) {
        $source = Join-Path $PatchRoot $relativePath
        $destination = Join-Path $ProjectRoot $relativePath
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }

    Write-Host 'Stage 1/3: Rebuilding Himo.Api (Release, forced Rebuild)...' -ForegroundColor Cyan
    $apiBuildStartedUtc = [DateTime]::UtcNow
    & dotnet build (Join-Path $ProjectRoot 'Himo.Api\Himo.Api.csproj') -c Release -t:Rebuild -m:1
    if ($LASTEXITCODE -ne 0) {
        throw "Himo.Api Release build failed with exit code $LASTEXITCODE."
    }
    $apiDll = Join-Path $ProjectRoot 'Himo.Api\bin\Release\net10.0\Himo.Api.dll'
    if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf) -or (Get-Item -LiteralPath $apiDll).Length -le 0) {
        throw "Himo.Api build returned success but Himo.Api.dll is missing or empty: $apiDll"
    }
    $apiDllInfo = Get-Item -LiteralPath $apiDll
    if ($apiDllInfo.LastWriteTimeUtc -lt $apiBuildStartedUtc.AddSeconds(-2)) {
        throw "Himo.Api.dll is older than this forced rebuild; refusing to accept a stale assembly: $apiDll"
    }

    Write-Host 'Stage 2/3: Rebuilding Himo Android (Release, net10.0-android36.0, forced Rebuild)...' -ForegroundColor Cyan
    $androidBuildStartedUtc = [DateTime]::UtcNow
    & dotnet build (Join-Path $ProjectRoot 'Himo.csproj') -c Release -f net10.0-android36.0 -t:Rebuild -m:1
    if ($LASTEXITCODE -ne 0) {
        throw "Himo Android Release build failed with exit code $LASTEXITCODE."
    }

    $apkRoot = Join-Path $ProjectRoot 'bin\Release\net10.0-android36.0'
    $apk = Get-ChildItem -LiteralPath $apkRoot -Filter '*.apk' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $androidBuildStartedUtc.AddSeconds(-2) } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $apk -or $apk.Length -le 0) {
        throw "Android build returned success but no fresh non-empty Release APK was produced after the forced rebuild started: $apkRoot"
    }
    Write-Host "Release APK found: $($apk.FullName) ($($apk.Length) bytes)" -ForegroundColor Cyan

    $verify16Kb = Join-Path $ProjectRoot 'Tools\Verify-Android16KB.ps1'
    if (-not (Test-Path -LiteralPath $verify16Kb -PathType Leaf)) {
        throw "Required Android 16 KB verification script is missing: $verify16Kb"
    }
    Write-Host 'Stage 3/3: Verifying WebRTC native-library 16 KB page alignment...' -ForegroundColor Cyan
    & $verify16Kb -ApkPath $apk.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Android 16 KB verification failed with exit code $LASTEXITCODE."
    }

    Write-Host ''
    Write-Host 'BUILD PASS: API DLL, Release APK and Android 16 KB native-library alignment verified. No install command was run.' -ForegroundColor Green
    Write-Host "Backup retained at: $backupDir"
    Write-Host "APK SHA256: $((Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash)"
}
catch {
    Write-Host ''
    Write-Host "PATCH/BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Restoring all touched source files from the backup; no app was installed.' -ForegroundColor Yellow
    foreach ($relativePath in $relativeFiles) {
        $backup = Join-Path $backupDir $relativePath
        $destination = Join-Path $ProjectRoot $relativePath
        if (Test-Path -LiteralPath $backup -PathType Leaf) {
            Copy-Item -LiteralPath $backup -Destination $destination -Force
        }
    }
    Write-Host "Backup retained at: $backupDir"
    exit 1
}
