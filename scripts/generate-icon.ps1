#requires -Version 7.0
. "$PSScriptRoot/common.ps1"
Push-Location $repoRoot
try {
    Invoke-Dotnet run --project tools/IconGenerator -c Release -- (Join-Path $repoRoot 'src/CodexUsageMonitor/Assets')
}
finally { Pop-Location }
