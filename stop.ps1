<#
.SYNOPSIS
    Stops the Radar services started by start.ps1 (API, AI service, frontend).

.DESCRIPTION
    1. Stops the windows recorded by start.ps1 in .run\services.json, together with all their child processes.
    2. Then, for each port (5149, 8001, 4200), stops whatever still listens on it - but only if it belongs to this
       project (its command line contains this folder). Any other program on those ports is reported and left alone.
    3. Stops leftover AI-service Python processes of this project (e.g. a uvicorn --reload worker).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\stop.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$Root = $PSScriptRoot
$StateFile = Join-Path $Root '.run\services.json'
$Ports = [ordered]@{ 'API' = 5149; 'AI service' = 8001; 'Frontend' = 4200 }

function Write-Ok([string]$Text)   { Write-Host "  [OK]   $Text" -ForegroundColor Green }
function Write-Info([string]$Text) { Write-Host "  [..]   $Text" -ForegroundColor Gray }
function Write-Warn([string]$Text) { Write-Host "  [WARN] $Text" -ForegroundColor Yellow }

function Stop-Tree([int]$ProcessId, [string]$What) {
    if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) { return $false }
    # /T stops the process and all its children (dotnet -> API, npm -> node, python workers)
    $null = & taskkill.exe /PID $ProcessId /T /F 2>&1
    Write-Ok "Stopped $What (PID $ProcessId)"
    return $true
}

function Get-ListeningPids([int]$Port) {
    # TCP and TCPv6: the Angular dev server listens on [::1]:4200 only
    $lines = & netstat.exe -ano 2>$null
    $pids = foreach ($line in $lines) {
        $parts = ($line.Trim() -split '\s+')
        if ($parts.Count -ge 5 -and $parts[0] -like 'TCP*' -and $parts[3] -eq 'LISTENING' -and $parts[1] -match "[:.]$Port$") {
            [int]$parts[4]
        }
    }
    return @($pids | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
}

function Get-CommandLine([int]$ProcessId) {
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    if ($p) { return [string]$p.CommandLine } else { return '' }
}

function Test-OwnedByProject([int]$ProcessId) {
    # the process itself, or one of its parents, was started from this folder
    $current = $ProcessId
    for ($i = 0; $i -lt 4 -and $current -gt 0; $i++) {
        $p = Get-CimInstance Win32_Process -Filter "ProcessId=$current" -ErrorAction SilentlyContinue
        if (-not $p) { break }
        if ([string]$p.CommandLine -like "*$Root*") { return $true }
        $current = [int]$p.ParentProcessId
    }
    return $false
}

Write-Host ""
Write-Host "==> Stopping Radar services" -ForegroundColor Cyan
$stoppedAny = $false

# 1. the windows started by start.ps1
if (Test-Path $StateFile) {
    try {
        $state = Get-Content $StateFile -Raw | ConvertFrom-Json
        foreach ($name in 'Api', 'Ai', 'Frontend') {
            $id = $state.Services.$name
            if ($id -and (Stop-Tree ([int]$id) "$name window")) { $stoppedAny = $true }
        }
    } catch {
        Write-Warn "Could not read $StateFile ($($_.Exception.Message)); continuing with the ports."
    }
    Remove-Item $StateFile -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Milliseconds 800

# 2. anything of this project still listening on the service ports
foreach ($entry in $Ports.GetEnumerator()) {
    foreach ($id in @(Get-ListeningPids $entry.Value)) {
        if (Test-OwnedByProject $id) {
            if (Stop-Tree $id "$($entry.Key) on port $($entry.Value)") { $stoppedAny = $true }
        } else {
            $name = (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName
            Write-Warn "Port $($entry.Value) is used by another program ($name, PID $id). Not touched."
        }
    }
}

# 3. leftover AI-service Python processes of this project (e.g. uvicorn --reload started by hand)
$aiFolder = Join-Path $Root 'ai-service'
Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { [string]$_.CommandLine -like "*$aiFolder*" } |
    ForEach-Object { if (Stop-Tree ([int]$_.ProcessId) 'AI service process') { $stoppedAny = $true } }

Start-Sleep -Milliseconds 800
$stillBusy = @($Ports.Values | Where-Object { @(Get-ListeningPids $_).Count -gt 0 })
if (-not $stoppedAny) { Write-Info "Nothing of Radar was running." }
if ($stillBusy.Count -eq 0) {
    Write-Ok "Ports 5149, 8001 and 4200 are free."
} else {
    Write-Warn "Still in use: $($stillBusy -join ', '). See 'Troubleshooting' in README.md."
}
Write-Host ""
