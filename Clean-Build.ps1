$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host 'أغلق Visual Studio بالكامل قبل تشغيل هذا السكربت.'
Write-Host 'سيتم حذف bin و obj و .vs فقط من مجلد v9.'

$targets = Get-ChildItem -Path $root -Directory -Force -Recurse |
    Where-Object { $_.Name -in @('bin', 'obj', '.vs') } |
    Sort-Object FullName -Descending

foreach ($dir in $targets) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            if (Test-Path -LiteralPath $dir.FullName) {
                Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction Stop
            }
            break
        }
        catch {
            if ($attempt -eq 5) {
                throw "تعذر حذف المجلد: $($dir.FullName)`n$($_.Exception.Message)"
            }
            Start-Sleep -Milliseconds (500 * $attempt)
        }
    }
}
Write-Host 'تم التنظيف.'
