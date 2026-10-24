<#
.SYNOPSIS
  Generates, builds and starts one of the UI test sites (shop or secure) on a free port.

.DESCRIPTION
  1. Stops any site still recorded in the target folder.
  2. Runs tools\harness, which recreates <Target>\app.db from scratch for the scenario, sets the
     project's security settings, and calls ProjectGenerator.Generate into <Target>\code.
  3. dotnet build of the generated project.
  4. Starts the built site on a free loopback port and waits until it accepts connections.
  The base URL is printed as UI_BASE_URL=... and recorded in <Target>\site.json.

.PARAMETER Target
  Folder for the generated site, database and logs. It is wiped and recreated on every run.

.PARAMETER Scenario
  designed : categories and products (30 rows) with per-page designs (cards, modal add/edit/view, offcanvas delete),
           lookup, currency, badge, row style, pattern validation, and en/hu localization (manual Hungarian entries).
  relations : customers (text key), orders, order_lines (composite key), categories, products with master/detail links
           (restrict, cascade, set null; expandable row, offcanvas and modal previews) and multi-page forms (tabs, pills, wizard, accordion).
  reports : regions and 24 sales (Unix-time stamp column) with a summary (date range and pick-list filters, drill-down),
           the filter builder, quick filter and reset button on the sales list,
           a chart, a crosstab, a calendar and a dashboard. No security.
  shop   : customers and orders tables, no security.
  secure : users (username, password, level), notes (owner = user name), reports (EditMinLevel 2),
           login, registration and change password enabled, failed-attempt lockout after 5 failures.

.PARAMETER PasswordHashFile
  File holding a password hash for alice and bob (both use secret-pass-123). Defaults to the
  UI_PASSWORD_HASH_FILE environment variable, or the scratchpad copy used by this project.

.EXAMPLE
  .\generate-site.ps1 -Target ..\.sites\shop -Scenario shop
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Target,

    [Parameter(Mandatory)]
    [ValidateSet('shop', 'secure', 'designed', 'relations', 'reports', 'audit')]
    [string]$Scenario,

    [string]$PasswordHashFile
)

$ErrorActionPreference = 'Stop'
$Target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Target)
$harness = Join-Path $PSScriptRoot 'harness\harness.csproj'
$stopScript = Join-Path $PSScriptRoot 'stop-site.ps1'

if (-not $PasswordHashFile) {
    $PasswordHashFile = $env:UI_PASSWORD_HASH_FILE
}
if ($PasswordHashFile) {
    $PasswordHashFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PasswordHashFile)
}
if ($Scenario -eq 'secure' -and (-not $PasswordHashFile -or -not (Test-Path -LiteralPath $PasswordHashFile))) {
    throw "The 'secure' scenario needs a password hash file: pass -PasswordHashFile or set UI_PASSWORD_HASH_FILE (see tests/ui/README.md)."
}
if (-not $PasswordHashFile -or -not (Test-Path -LiteralPath $PasswordHashFile)) {
    # Only the secure scenario uses the hash; the harness still wants a non-empty file argument.
    $PasswordHashFile = Join-Path ([IO.Path]::GetTempPath()) 'ui-unused-hash.txt'
    Set-Content -LiteralPath $PasswordHashFile -Value 'unused'
}
if (-not (Test-Path -LiteralPath $PasswordHashFile)) {
    throw "Password hash file not found: $PasswordHashFile (set UI_PASSWORD_HASH_FILE or pass -PasswordHashFile)"
}

# 1. Stop a previous run that is still serving this folder.
& $stopScript -Target $Target

# 2. Recreate the database and the generated code.
Write-Host "Generating '$Scenario' into $Target"
dotnet run --project $harness --no-launch-profile -- $Scenario $Target $PasswordHashFile
if ($LASTEXITCODE -ne 0) { throw "Harness failed with exit code $LASTEXITCODE" }

# 3. Build the generated project.
$csproj = Get-ChildItem -LiteralPath (Join-Path $Target 'code') -Filter '*.csproj' | Select-Object -First 1
if ($null -eq $csproj) { throw "No generated .csproj under $Target\code" }
$appName = [IO.Path]::GetFileNameWithoutExtension($csproj.Name)
dotnet build $csproj.FullName -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build of the generated site failed" }

$exe = Join-Path $Target "code\bin\Debug\net10.0\$appName.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    # macOS and Linux build the app host without an extension.
    $exe = Join-Path $Target "code/bin/Debug/net10.0/$appName"
}
if (-not (Test-Path -LiteralPath $exe)) { throw "Built program not found: $exe" }

# 4. Start on a free loopback port.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"

$previousUrls = $env:ASPNETCORE_URLS
$env:ASPNETCORE_URLS = $baseUrl
try {
    $proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $Target 'site.out.log') -RedirectStandardError (Join-Path $Target 'site.err.log')
} finally {
    $env:ASPNETCORE_URLS = $previousUrls
}

# 5. Wait until the port accepts connections.
$ready = $false
for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
    if ($proc.HasExited) { throw "Site exited early; see $Target\site.err.log" }
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', $port)
        $ready = $true
    } catch {
        Start-Sleep -Milliseconds 250
    } finally {
        $client.Dispose()
    }
}
if (-not $ready) {
    Stop-Process -Id $proc.Id -Force
    throw "Site did not start on $baseUrl within 15 seconds"
}

[pscustomobject]@{
    scenario = $Scenario
    pid      = $proc.Id
    port     = $port
    baseUrl  = $baseUrl
    exe      = $exe
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Target 'site.json') -Encoding utf8

Write-Host "Site '$Scenario' is running (pid $($proc.Id))"
Write-Host "UI_BASE_URL=$baseUrl"
