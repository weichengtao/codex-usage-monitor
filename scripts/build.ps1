#requires -Version 7.0
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. "$PSScriptRoot/common.ps1"
Push-Location $repoRoot
try { Invoke-Dotnet build src/CodexUsageMonitor/CodexUsageMonitor.csproj -c $Configuration }
finally { Pop-Location }
