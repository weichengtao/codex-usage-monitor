#requires -Version 7.0
param([switch]$Live)
. "$PSScriptRoot/common.ps1"
Push-Location $repoRoot
try {
    Invoke-Dotnet run --project tests/CodexUsageMonitor.Tests -c Release
    Invoke-Dotnet run --project tests/CodexUsageMonitor.TrayTests -c Release
    Invoke-Dotnet build src/CodexUsageMonitor/CodexUsageMonitor.csproj -c Release
    $output = Join-Path $repoRoot 'artifacts/checks'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $exe = Join-Path $repoRoot 'src/CodexUsageMonitor/bin/Release/net10.0-windows/CodexUsageMonitor.exe'
    & $exe --render-check $output | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Rendering or native handle checks failed.' }
    Get-Content (Join-Path $output 'render-check.json')
    if ($Live) {
        & $exe --probe (Join-Path $output 'live-usage.json') | Out-Null
        Get-Content (Join-Path $output 'live-usage.json')
        if ($LASTEXITCODE -ne 0) { throw 'Live Codex usage probe failed.' }
    }
}
finally { Pop-Location }
