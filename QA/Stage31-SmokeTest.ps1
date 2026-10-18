[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$passed = 0
$failed = 0

function Assert-Equal([string]$Name, $Actual, $Expected) {
    if ($Actual -eq $Expected) {
        Write-Host "PASS  $Name"
        $script:passed++
    } else {
        Write-Host "FAIL  $Name (expected $Expected, got $Actual)" -ForegroundColor Red
        $script:failed++
    }
}

function Get-Status([string]$Method, [string]$Uri, [hashtable]$Headers = @{}) {
    try {
        $response = Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -SkipHttpErrorCheck -UseBasicParsing
        return [int]$response.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode.value__ }
        throw
    }
}

Write-Host "Stage 31 smoke test: $BaseUrl"

$health = Invoke-WebRequest -Method GET -Uri "$BaseUrl/health" -UseBasicParsing
Assert-Equal "Health endpoint" ([int]$health.StatusCode) 200
Assert-Equal "Health payload" (($health.Content | ConvertFrom-Json).status) "ok"

Assert-Equal "Unauthenticated /api/me" (Get-Status GET "$BaseUrl/api/me") 401
Assert-Equal "Malformed bearer /api/me" (Get-Status GET "$BaseUrl/api/me" @{ Authorization = "Bearer invalid-token" }) 401
Assert-Equal "Unauthenticated conversations" (Get-Status GET "$BaseUrl/api/conversations") 401

Write-Host ""
Write-Host "Passed: $passed"
Write-Host "Failed: $failed"

if ($failed -gt 0) { exit 1 }
exit 0
