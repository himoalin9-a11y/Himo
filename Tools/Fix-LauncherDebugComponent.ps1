$ErrorActionPreference = 'Stop'
$packageName = 'com.companyname.himo'
$component = "$packageName/$packageName.LauncherIconDefault"

$candidates = @()
if ($env:ANDROID_HOME) { $candidates += (Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe') }
if ($env:ANDROID_SDK_ROOT) { $candidates += (Join-Path $env:ANDROID_SDK_ROOT 'platform-tools\adb.exe') }
$candidates += 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
$candidates += (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe')
$adb = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $adb) { throw 'لم يتم العثور على adb.exe. تحقق من تثبيت Android SDK.' }

$lines = & $adb devices
$devices = @($lines | Select-Object -Skip 1 | ForEach-Object {
    if ($_ -match '^\s*(\S+)\s+device\s*$') { $Matches[1] }
})
if ($devices.Count -eq 0) { throw 'لا يوجد هاتف متصل مع تفعيل USB debugging.' }
if ($devices.Count -gt 1) {
    Write-Host 'الأجهزة المتصلة:'
    for ($i = 0; $i -lt $devices.Count; $i++) { Write-Host "[$i] $($devices[$i])" }
    $choice = Read-Host 'اكتب رقم الهاتف المطلوب'
    if ($choice -notmatch '^\d+$' -or [int]$choice -ge $devices.Count) { throw 'اختيار الجهاز غير صحيح.' }
    $serial = $devices[[int]$choice]
} else { $serial = $devices[0] }

Write-Host "تفعيل مكوّن تشغيل Himo الثابت على الجهاز $serial ..."
& $adb -s $serial shell pm enable --user 0 $component
if ($LASTEXITCODE -ne 0) { throw 'تعذر تفعيل المكوّن. تأكد أن النسخة المعدّلة من Himo قد ثُبتت على الهاتف.' }
Write-Host 'تم تفعيل المكوّن. أعد تشغيل جلسة Debug في Visual Studio.' -ForegroundColor Green
