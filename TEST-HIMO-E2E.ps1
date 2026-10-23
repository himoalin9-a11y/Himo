$ErrorActionPreference = 'Stop'
$base = 'https://himo-3buh.onrender.com'

function Test-Endpoint($Path, $ExpectedStatus) {
    try {
        $r = Invoke-WebRequest -Uri ($base + $Path) -Method Get -UseBasicParsing -SkipHttpErrorCheck
        if ([int]$r.StatusCode -ne $ExpectedStatus) {
            throw "$Path returned $($r.StatusCode), expected $ExpectedStatus"
        }
        Write-Host "PASS $Path -> $($r.StatusCode)"
    }
    catch {
        Write-Error "FAIL $Path : $($_.Exception.Message)"
    }
}

Test-Endpoint '/health' 200
Test-Endpoint '/health/database' 200
Test-Endpoint '/api/me' 401
Write-Host 'Smoke gate completed. Authenticated E2E tests require a real test account and are intentionally manual.'
