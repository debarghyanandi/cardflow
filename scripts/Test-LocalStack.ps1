[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Compose {
    param([string[]]$ComposeArguments)

    & docker compose --project-directory $repoRoot @ComposeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($ComposeArguments -join ' ') failed ($LASTEXITCODE)."
    }
}

function Read-Health {
    param([string]$Path = '/health')

    $response = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:8080$Path" -TimeoutSec 15
    if ($response.StatusCode -ne 200) {
        throw "$Path returned $($response.StatusCode), expected 200."
    }
    $report = $response.Content | ConvertFrom-Json
    if ($report.status -ne 'Healthy') {
        throw "$Path reported $($report.status), expected Healthy."
    }
    if ($report.instance -notin @('api-1', 'api-2')) {
        throw "Unexpected instance name: $($report.instance)."
    }
    if ($Path -eq '/health' -and
        ($report.checks.postgres -ne 'Healthy' -or $report.checks.redis -ne 'Healthy')) {
        throw 'The full health report must include healthy Postgres and Redis checks.'
    }
    if ($Path -eq '/health/live' -and @($report.checks.PSObject.Properties).Count -ne 0) {
        throw 'Liveness must not run dependency checks.'
    }
    return $report
}

function Assert-BothInstances {
    # Bound the loop, but allow nginx's five-second passive failure window to expire.
    $seen = @{}
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $report = Read-Health
        $seen[$report.instance] = $true
        if ($seen.Count -eq 2) {
            Write-Host 'PASS: /health through nginx reaches api-1 and api-2.'
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "nginx did not reach both APIs. Saw: $($seen.Keys -join ', ')."
}

Invoke-Compose -ComposeArguments @('up', '-d', '--wait', '--wait-timeout', '120')

$configuration = (Invoke-Compose -ComposeArguments @('config', '--format', 'json')) -join "`n" |
    ConvertFrom-Json
foreach ($name in @('api-1', 'api-2')) {
    $service = $configuration.services.$name
    if ($service.environment.InstanceName -ne $name -or
        $service.healthcheck.test[-1] -ne 'http://localhost:8080/health/live') {
        throw "$name must have its own instance name and use /health/live for container health."
    }
}
Write-Host 'PASS: both API container health checks use /health/live.'

Assert-BothInstances
$null = Read-Health -Path '/health/live'
$mailpit = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:8025' -TimeoutSec 10
if ($mailpit.StatusCode -ne 200 -or $mailpit.Content -notmatch 'Mailpit') {
    throw 'Mailpit did not serve its web interface.'
}
Write-Host 'PASS: Mailpit web interface returns 200.'

# Tokens travel in the query string on /hubs/board, so nginx must not log query strings.
$marker = "logcheck$([Guid]::NewGuid().ToString('N'))"
$null = Read-Health -Path "/health/live?access_token=$marker"
$nginxLog = (Invoke-Compose -ComposeArguments @('logs', 'nginx')) -join "`n"
if ($nginxLog -match $marker) {
    throw 'nginx logged a query string. Tokens would leak into the logs.'
}
Write-Host 'PASS: nginx access log leaves out query strings.'

try {
    Invoke-Compose -ComposeArguments @('stop', 'api-1')
    # Do not retry client failures: nginx itself must retry the failed upstream.
    1..12 | ForEach-Object {
        $report = Read-Health
        if ($report.instance -ne 'api-2') {
            throw 'Requests must reach api-2 while api-1 is stopped.'
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Host 'PASS: 12 requests returned 200 from api-2 with api-1 stopped.'
}
finally {
    Invoke-Compose -ComposeArguments @('up', '-d', '--wait', '--wait-timeout', '120', 'api-1')
}

Assert-BothInstances
Write-Host 'PASS: BE-0.2 local stack checks passed; both APIs are running.'
