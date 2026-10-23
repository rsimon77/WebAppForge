<#
.SYNOPSIS
  Stops the site that generate-site.ps1 started in a target folder.

.PARAMETER Target
  The folder that was passed to generate-site.ps1. The site's process id is read from <Target>\site.json.

.EXAMPLE
  .\stop-site.ps1 -Target ..\.sites\shop
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Target
)

$ErrorActionPreference = 'Stop'
$Target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Target)
$stateFile = Join-Path $Target 'site.json'

if (-not (Test-Path -LiteralPath $stateFile)) {
    Write-Host "No running site recorded in $Target"
    return
}

$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
$process = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
if ($null -ne $process -and $process.Path -eq $state.exe) {
    Stop-Process -Id $process.Id -Force
    Write-Host "Stopped site '$($state.scenario)' (pid $($state.pid), $($state.baseUrl))"
} else {
    Write-Host "Site '$($state.scenario)' (pid $($state.pid)) is not running"
}

Remove-Item -LiteralPath $stateFile -Force
