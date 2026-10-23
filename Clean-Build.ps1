$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Split-Path -Parent $MyInvocation.MyCommand.Path)).Path

Write-Host 'إغلاق Visual Studio مطلوب قبل حذف ملفات البناء.'
$activeBuildProcesses = Get-Process -Name devenv, MSBuild -ErrorAction SilentlyContinue
if ($activeBuildProcesses) {
    $names = ($activeBuildProcesses | Select-Object -ExpandProperty ProcessName -Unique) -join ', '
    throw "أغلق العمليات التالية ثم أعد التشغيل: $names"
}

Write-Host 'سيحذف هذا السكربت مجلدات bin و obj و .vs داخل المشروع ومشاريعه الفرعية فقط.'
Write-Host 'لن يعدّل ملفات المصدر أو الموارد أو بيانات التطبيق على الهاتف.'
$answer = Read-Host 'اكتب CLEAN للمتابعة'
if ($answer -cne 'CLEAN') {
    Write-Host 'تم الإلغاء؛ لم يتم حذف أي ملف.'
    exit 0
}

$targets = Get-ChildItem -LiteralPath $root -Directory -Force -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('bin', 'obj', '.vs') } |
    Sort-Object { $_.FullName.Length } -Descending

foreach ($dir in $targets) {
    $path = $dir.FullName
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
    if (-not $path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "فشل فحص الأمان للمسار: $path"
    }

    $removed = $false
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            if (Test-Path -LiteralPath $path -PathType Container) {
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            }
            $removed = -not (Test-Path -LiteralPath $path)
            if ($removed) { break }
        }
        catch {
            if ($attempt -eq 5) {
                throw "تعذر حذف مجلد البناء: $path`n$($_.Exception.Message)`nتأكد من إغلاق Visual Studio وكل عمليات البناء."
            }
            Start-Sleep -Milliseconds (400 * $attempt)
        }
    }
    if ($removed) { Write-Host ("تم حذف: " + $path) }
}

$remaining = Get-ChildItem -LiteralPath $root -Directory -Force -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('bin', 'obj', '.vs') }
if ($remaining) {
    $paths = ($remaining | Select-Object -ExpandProperty FullName) -join "`n"
    throw "بقيت مجلدات بناء لم تُحذف:`n$paths"
}
Write-Host 'اكتمل تنظيف ملفات البناء. أعد فتح Himo.sln ونفّذ Restore ثم Clean Solution وRebuild Solution.' -ForegroundColor Green
