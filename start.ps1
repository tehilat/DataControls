<#
.SYNOPSIS
    Starts Radar (data-quality controls) with one command: checks prerequisites, creates the database if needed,
    installs dependencies, starts the API, the AI service and the frontend, and opens the browser.

.DESCRIPTION
    Steps:
      1. Prerequisites: .NET 10 SDK, Node.js (as required by Angular 22), Python 3.11+, sqlcmd - with a clear
         message and a link when something is missing.
      2. SQL Server: the instance given in -SqlInstance, otherwise the first one that answers of
         localhost\SQLEXPRESS, localhost, other local named instances, and LocalDB ((localdb)\MSSQLLocalDB).
      3. Database DroneOpsControls: created from database\01-05 only if it does not exist yet (your controls and
         runs are kept), then checked with 99_verify.sql. -ResetDatabase recreates it from scratch.
      4. Dependencies: dotnet build, a Python venv with requirements.txt, npm install - each only when needed.
      5. Starts the three services, each in its own minimized window, waits until they answer, opens the browser.

    Stop everything with:  powershell -ExecutionPolicy Bypass -File .\stop.ps1

.PARAMETER SqlInstance
    SQL Server instance, e.g. "localhost\SQLEXPRESS", "localhost", "(localdb)\MSSQLLocalDB". Default: auto-detect.

.PARAMETER ResetDatabase
    Recreate the database from scratch. Deletes the controls and runs you created.

.PARAMETER NoBrowser
    Do not open the browser at the end.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\start.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\start.ps1 -SqlInstance "(localdb)\MSSQLLocalDB"
#>
[CmdletBinding()]
param(
    [string]$SqlInstance,
    [switch]$ResetDatabase,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$ProgressPreference = 'SilentlyContinue'

$Root = $PSScriptRoot
$RunDir = Join-Path $Root '.run'
$StateFile = Join-Path $RunDir 'services.json'
$DbName = 'DroneOpsControls'
$ApiUrl = 'http://localhost:5149'
$AiUrl = 'http://localhost:8001'
$FrontendUrl = 'http://localhost:4200'
$Started = Get-Date

# ------------------------------------------------------------------ output helpers

function Write-Step([string]$Text) { Write-Host ""; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "  [OK]   $Text" -ForegroundColor Green }
function Write-Info([string]$Text) { Write-Host "  [..]   $Text" -ForegroundColor Gray }
function Write-Warn([string]$Text) { Write-Host "  [WARN] $Text" -ForegroundColor Yellow }
function Stop-WithError([string]$Text, [string[]]$Hints = @()) {
    Write-Host "  [FAIL] $Text" -ForegroundColor Red
    foreach ($hint in $Hints) { Write-Host "         $hint" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "Radar was not started. Fix the problem above and run start.ps1 again." -ForegroundColor Red
    exit 1
}

# Runs a native program; returns exit code and combined output without throwing on stderr (PowerShell 5.1).
function Invoke-Native([string]$File, [string[]]$Arguments = @(), [string]$WorkingDirectory = $Root) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    Push-Location $WorkingDirectory
    try {
        $output = & $File @Arguments 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ Code = $LASTEXITCODE; Output = (@($output) -join "`n") }
    } finally {
        Pop-Location
        $ErrorActionPreference = $previous
    }
}

function Get-Tail([string]$Text, [int]$Lines = 15) {
    return (($Text -split "`n") | Where-Object { $_.Trim() } | Select-Object -Last $Lines) -join "`n"
}

# ------------------------------------------------------------------ 1. prerequisites

Write-Step 'Checking prerequisites'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-WithError '.NET SDK not found.' @('Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0')
}
$sdks = (Invoke-Native 'dotnet' @('--list-sdks')).Output -split "`n" | Where-Object { $_ -match '^\s*10\.' }
if (-not $sdks) {
    Stop-WithError '.NET 10 SDK not found (another .NET version is installed).' @('Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0')
}
Write-Ok ".NET SDK $((@($sdks)[-1] -split ' ')[0])"

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Stop-WithError 'Node.js not found.' @('Install Node.js 24 LTS: https://nodejs.org')
}
$nodeText = (Invoke-Native 'node' @('--version')).Output.Trim().TrimStart('v')
$node = [version]($nodeText -replace '[^0-9.].*$', '')
$nodeOk = ($node.Major -eq 22 -and $node -ge [version]'22.22.3') -or ($node.Major -eq 24 -and $node -ge [version]'24.15.0') -or ($node.Major -ge 26)
if (-not $nodeOk) {
    Stop-WithError "Node.js $nodeText is not supported by Angular 22 (needs ^22.22.3, ^24.15.0 or >= 26)." @('Install Node.js 24 LTS: https://nodejs.org')
}
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    Stop-WithError 'npm not found (it is installed with Node.js).' @('Reinstall Node.js 24 LTS: https://nodejs.org')
}
Write-Ok "Node.js $nodeText"

# python on PATH, or the "py" launcher; the Microsoft Store alias "python" prints nothing and is skipped
$Python = $null
$pythonCandidates = @(@{ Exe = 'python'; Args = @() }, @{ Exe = 'py'; Args = @('-3') })
foreach ($candidate in $pythonCandidates) {
    if (-not (Get-Command $candidate.Exe -ErrorAction SilentlyContinue)) { continue }
    $version = (Invoke-Native $candidate.Exe (@($candidate.Args) + '--version')).Output
    if ($version -match 'Python (\d+)\.(\d+)(\.\d+)?') {
        if ([int]$Matches[1] -eq 3 -and [int]$Matches[2] -ge 11) {
            $Python = $candidate
            Write-Ok "$($version.Trim())"
            break
        }
        Write-Warn "$($version.Trim()) found, but 3.11 or newer is needed."
    }
}
if (-not $Python) {
    Stop-WithError 'Python 3.11 or newer not found.' @(
        'Install Python 3.13: https://www.python.org/downloads/ (check "Add python.exe to PATH").',
        'If "python" opens the Microsoft Store: Settings > Apps > Advanced app settings > App execution aliases > turn off python.exe.')
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Stop-WithError 'sqlcmd not found.' @('Install it: winget install Microsoft.Sqlcmd', 'or https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility')
}
Write-Ok 'sqlcmd'

# ------------------------------------------------------------------ 2. SQL Server instance

Write-Step 'Finding SQL Server'

function Test-SqlInstance([string]$Instance) {
    $result = Invoke-Native 'sqlcmd' @('-S', $Instance, '-E', '-C', '-b', '-l', '5', '-h', '-1', '-Q', 'SET NOCOUNT ON; SELECT 1')
    return $result.Code -eq 0
}

$candidates = New-Object System.Collections.Generic.List[string]
if ($SqlInstance) {
    $candidates.Add($SqlInstance)
} else {
    $candidates.Add('localhost\SQLEXPRESS')
    $services = @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'MSSQLSERVER' -or $_.Name -like 'MSSQL$*' })
    if ($services | Where-Object Name -eq 'MSSQLSERVER') { $candidates.Add('localhost') }
    foreach ($service in $services | Where-Object { $_.Name -like 'MSSQL$*' }) {
        $instance = "localhost\$($service.Name.Substring(6))"
        if (-not $candidates.Contains($instance)) { $candidates.Add($instance) }
    }
    if (Get-Command sqllocaldb -ErrorAction SilentlyContinue) {
        $null = Invoke-Native 'sqllocaldb' @('start', 'MSSQLLocalDB')   # creates it on first use
        $candidates.Add('(localdb)\MSSQLLocalDB')
    }
}

$Instance = $null
foreach ($candidate in $candidates) {
    Write-Info "Trying $candidate"
    if (Test-SqlInstance $candidate) { $Instance = $candidate; break }
}
if (-not $Instance) {
    $hints = @()
    if ($SqlInstance) { $hints += "Could not connect to '$SqlInstance' with Windows authentication." }
    $stopped = @(Get-Service -ErrorAction SilentlyContinue | Where-Object { ($_.Name -eq 'MSSQLSERVER' -or $_.Name -like 'MSSQL$*') -and $_.Status -ne 'Running' })
    foreach ($service in $stopped) { $hints += "Service $($service.Name) is $($service.Status). Start it (as admin): Start-Service '$($service.Name)'" }
    $hints += 'Install SQL Server Express (or LocalDB): https://www.microsoft.com/sql-server/sql-server-downloads'
    $hints += 'Or pass your instance: .\start.ps1 -SqlInstance "<server\instance>"'
    Stop-WithError 'No SQL Server instance answered.' $hints
}
Write-Ok "SQL Server: $Instance"
$ConnectionString = "Server=$Instance;Database=$DbName;Trusted_Connection=True;TrustServerCertificate=True;"

# ------------------------------------------------------------------ services that are already running

$ports = @{ 5149 = 'API'; 8001 = 'AI service'; 4200 = 'Frontend' }
function Get-ListeningPids([int]$Port) {
    $lines = & netstat.exe -ano 2>$null
    $found = foreach ($line in $lines) {
        $parts = ($line.Trim() -split '\s+')
        if ($parts.Count -ge 5 -and $parts[0] -like 'TCP*' -and $parts[3] -eq 'LISTENING' -and $parts[1] -match "[:.]$Port$") { [int]$parts[4] }
    }
    return @($found | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
}
$busy = @($ports.Keys | Where-Object { @(Get-ListeningPids $_).Count -gt 0 })
if ($busy.Count -gt 0) {
    Write-Step 'Stopping a previous run'
    & (Join-Path $Root 'stop.ps1')
    $busy = @($ports.Keys | Where-Object { @(Get-ListeningPids $_).Count -gt 0 })
    if ($busy.Count -gt 0) {
        $names = ($busy | ForEach-Object { "$_ ($($ports[$_]))" }) -join ', '
        Stop-WithError "Port(s) in use by another program: $names." @("See 'Troubleshooting' in README.md (netstat -ano | findstr "":<port>"").")
    }
}

# ------------------------------------------------------------------ 3. database

Write-Step "Database $DbName"

function Invoke-SqlFile([string]$File) {
    $result = Invoke-Native 'sqlcmd' @('-S', $Instance, '-E', '-C', '-b', '-f', '65001', '-i', $File)
    if ($result.Code -ne 0) {
        Stop-WithError "$(Split-Path $File -Leaf) failed." @((Get-Tail $result.Output 10) -split "`n")
    }
}

$stateQuery = "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('$DbName') IS NULL THEN 0 WHEN OBJECT_ID('$DbName.ctl.Controls') IS NULL THEN 1 ELSE 2 END"
$dbState = (Invoke-Native 'sqlcmd' @('-S', $Instance, '-E', '-C', '-b', '-h', '-1', '-W', '-Q', $stateQuery)).Output.Trim()

if ($dbState -eq '2' -and -not $ResetDatabase) {
    Write-Ok 'Exists - kept as is (your controls and runs are kept). Use -ResetDatabase to recreate it.'
} else {
    if ($dbState -eq '2') { Write-Warn 'Recreating the database (-ResetDatabase): existing controls and runs are deleted.' }
    elseif ($dbState -eq '1') { Write-Info 'Database exists but is incomplete - recreating it.' }
    else { Write-Info 'Creating it from database\01-05.' }

    foreach ($script in Get-ChildItem (Join-Path $Root 'database') -Filter '0[1-5]*.sql' | Sort-Object Name) {
        Write-Info $script.Name
        Invoke-SqlFile $script.FullName
    }

    $verify = Invoke-Native 'sqlcmd' @('-S', $Instance, '-E', '-C', '-f', '65001', '-W', '-s', '|', '-i', (Join-Path $Root 'database\99_verify.sql'))
    $lines = @($verify.Output -split "`n" | ForEach-Object { $_.Trim() })
    $summaryIndex = [array]::IndexOf($lines, 'PassedControls|TotalControls')
    $summary = if ($summaryIndex -ge 0 -and $summaryIndex + 2 -lt $lines.Count) { $lines[$summaryIndex + 2] } else { '' }
    if ($summary -match '^(\d+)\|(\d+)$' -and $Matches[1] -eq $Matches[2]) {
        Write-Ok "Created and verified: $($Matches[1])/$($Matches[2]) sample controls return the expected records."
    } else {
        Stop-WithError "99_verify.sql did not pass (summary: '$summary')." @("Run it to see the details: sqlcmd -S $Instance -E -C -f 65001 -W -s ""|"" -i .\database\99_verify.sql")
    }
}

# ------------------------------------------------------------------ 4. dependencies

Write-Step 'Dependencies'

function Get-FileHashText([string]$Path) { return (Get-FileHash -Algorithm SHA256 $Path).Hash }

# backend
Write-Info 'Building the API (dotnet build)'
$build = Invoke-Native 'dotnet' @('build', 'DataControls.Api', '--nologo', '-v', 'q') (Join-Path $Root 'backend')
if ($build.Code -ne 0) {
    Stop-WithError 'The API build failed.' @((Get-Tail $build.Output 12) -split "`n")
}
Write-Ok 'API built'

# AI service: venv + requirements (only when requirements.txt changed)
$aiDir = Join-Path $Root 'ai-service'
$venvPython = Join-Path $aiDir '.venv\Scripts\python.exe'
if (-not (Test-Path $venvPython)) {
    Write-Info 'Creating the Python virtual environment (ai-service\.venv)'
    $venv = Invoke-Native $Python.Exe (@($Python.Args) + @('-m', 'venv', '.venv')) $aiDir
    if ($venv.Code -ne 0 -or -not (Test-Path $venvPython)) { Stop-WithError 'Could not create the Python venv.' @((Get-Tail $venv.Output 8) -split "`n") }
}
$reqMarker = Join-Path $aiDir '.venv\.radar-requirements'
$reqHash = Get-FileHashText (Join-Path $aiDir 'requirements.txt')
if (-not (Test-Path $reqMarker) -or (Get-Content $reqMarker -Raw).Trim() -ne $reqHash) {
    Write-Info 'Installing Python packages (pip install -r requirements.txt)'
    $pip = Invoke-Native $venvPython @('-m', 'pip', 'install', '--disable-pip-version-check', '-q', '-r', 'requirements.txt') $aiDir
    if ($pip.Code -ne 0) {
        Stop-WithError 'pip install failed.' (@((Get-Tail $pip.Output 8) -split "`n") + 'Behind a TLS-inspecting proxy: pip config set global.cert <path-to-ca.crt>  (never disable TLS verification).')
    }
    Set-Content -Path $reqMarker -Value $reqHash -Encoding ASCII
}
Write-Ok 'AI service packages'

# frontend: npm install (only when package-lock.json changed)
$feDir = Join-Path $Root 'frontend'
$npmMarker = Join-Path $feDir 'node_modules\.radar-package-lock'
$lockHash = Get-FileHashText (Join-Path $feDir 'package-lock.json')
if (-not (Test-Path $npmMarker) -or (Get-Content $npmMarker -Raw).Trim() -ne $lockHash) {
    Write-Info 'Installing frontend packages (npm install - takes a minute or two the first time)'
    $npm = Invoke-Native 'npm.cmd' @('install', '--no-audit', '--no-fund') $feDir
    if ($npm.Code -ne 0) {
        Stop-WithError 'npm install failed.' (@((Get-Tail $npm.Output 8) -split "`n") + 'Behind a TLS-inspecting proxy: npm config set cafile <path-to-ca.crt>  (never disable TLS verification).')
    }
    Set-Content -Path $npmMarker -Value $lockHash -Encoding ASCII
    Write-Info 'The npm "install scripts not yet covered by allowScripts" warning is expected and harmless (see README).'
}
Write-Ok 'Frontend packages'

# ------------------------------------------------------------------ 5. services

Write-Step 'Starting the services (each in its own minimized window)'

# -EncodedCommand: Windows PowerShell does not quote arguments with spaces, and the command contains some.
function Start-ServiceWindow([string]$Title, [string]$Directory, [string]$Command) {
    $script = "`$host.UI.RawUI.WindowTitle = '$Title'; Set-Location -LiteralPath '$Directory'; $Command"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $process = Start-Process powershell.exe -PassThru -WindowStyle Minimized `
        -ArgumentList '-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded
    return $process.Id
}

$escapedConnection = $ConnectionString.Replace("'", "''")
$apiPid = Start-ServiceWindow 'Radar - API (5149)' (Join-Path $Root 'backend') `
    "`$env:ConnectionStrings__DroneOps = '$escapedConnection'; dotnet run --no-build --project DataControls.Api --launch-profile http"
$aiPid = Start-ServiceWindow 'Radar - AI service (8001)' $aiDir `
    "& '.\.venv\Scripts\python.exe' -m uvicorn main:app --port 8001"
$fePid = Start-ServiceWindow 'Radar - Frontend (4200)' $feDir `
    "`$env:NG_CLI_ANALYTICS = 'false'; npm start"

New-Item -ItemType Directory -Force $RunDir | Out-Null
[pscustomobject]@{
    StartedAt   = (Get-Date).ToString('s')
    SqlInstance = $Instance
    Services    = [pscustomobject]@{ Api = $apiPid; Ai = $aiPid; Frontend = $fePid }
} | ConvertTo-Json | Set-Content -Path $StateFile -Encoding UTF8

function Wait-Http([string]$Url, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 5
            return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $response.Content }
        } catch [System.Net.WebException] {
            $response = $_.Exception.Response
            if ($response) {
                $reader = New-Object System.IO.StreamReader($response.GetResponseStream(), [Text.Encoding]::UTF8)
                return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $reader.ReadToEnd() }
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

# AI service - optional: the rest works without it
$ai = Wait-Http "$AiUrl/health" 60
$aiOk = $false
if ($null -eq $ai) {
    Write-Warn "AI service did not start (see the 'Radar - AI service' window). Stage B ('create with AI') is unavailable; everything else works."
} elseif ($ai.Status -eq 200) {
    Write-Ok "AI service   $AiUrl  (provider: $(($ai.Body | ConvertFrom-Json).provider))"
    $aiOk = $true
} else {
    # the 503 body is not always readable in Windows PowerShell; the provider comes from the same variable the service reads
    $provider = if ($env:AI_PROVIDER) { $env:AI_PROVIDER } else { 'mock' }
    Write-Warn "AI service is running but not configured (AI_PROVIDER='$provider', e.g. a missing API key)."
    Write-Warn "The 'create with AI' screen shows the reason; everything else works. Default AI_PROVIDER is 'mock' (no key needed)."
}

$api = Wait-Http "$ApiUrl/api/metadata/entities" 120
if ($null -eq $api -or $api.Status -ne 200) {
    Stop-WithError 'The API did not start.' @("Look at the 'Radar - API (5149)' window for the error.", 'Stop everything with: .\stop.ps1')
}
Write-Ok "API          $ApiUrl  (Swagger: $ApiUrl/swagger)"

Write-Info 'Waiting for the frontend (the first build takes up to a minute)'
$fe = Wait-Http "$FrontendUrl/" 240
if ($null -eq $fe -or $fe.Status -ne 200) {
    Stop-WithError 'The frontend did not start.' @("Look at the 'Radar - Frontend (4200)' window for the error.", 'Stop everything with: .\stop.ps1')
}
$proxy = Wait-Http "$FrontendUrl/api/metadata/entities" 30
if ($null -eq $proxy -or $proxy.Status -ne 200) {
    Write-Warn 'The frontend is up, but its /api proxy does not reach the API (check frontend\proxy.conf.json).'
}
Write-Ok "Frontend     $FrontendUrl"

# ------------------------------------------------------------------ done

$elapsed = [int]((Get-Date) - $Started).TotalSeconds
Write-Host ""
Write-Host "Radar is running ($elapsed s)." -ForegroundColor Green
Write-Host "  App:      $FrontendUrl"
Write-Host "  API:      $ApiUrl/swagger"
if ($aiOk) { Write-Host "  AI:       $AiUrl/docs" }
Write-Host "  Database: $DbName on $Instance"
Write-Host "  Stop:     powershell -ExecutionPolicy Bypass -File .\stop.ps1"
Write-Host ""

if (-not $NoBrowser) { Start-Process $FrontendUrl }
