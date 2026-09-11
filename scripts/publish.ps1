#requires -Version 7.0
param([switch]$SelfContained, [switch]$SingleFile)
. "$PSScriptRoot/common.ps1"
Push-Location $repoRoot
try {
    $variant = if ($SingleFile) { 'win-x64-single-file' } elseif ($SelfContained) { 'win-x64-self-contained' } else { 'win-x64' }
    $output = Join-Path $repoRoot "artifacts/publish/$variant"
    $bundledRuntime = $SelfContained.IsPresent -or $SingleFile.IsPresent
    $publishArgs = @('publish', 'src/CodexUsageMonitor/CodexUsageMonitor.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', $bundledRuntime.ToString().ToLowerInvariant(), '-o', $output)
    if ($SingleFile) {
        $publishArgs += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:IncludeAllContentForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=embedded')
    }
    Invoke-Dotnet @publishArgs
    if ($SingleFile) {
        $unexpected = Get-ChildItem -LiteralPath $output -File | Where-Object Name -NotIn @('CodexUsageMonitor.exe', 'README.md', 'LICENSE')
        if ($unexpected) { throw "Unexpected sidecar files in single-file output: $($unexpected.Name -join ', ')" }
    }
    Copy-Item README.md,LICENSE -Destination $output
    Compress-Archive -Path "$output/*" -DestinationPath (Join-Path $repoRoot "artifacts/CodexUsageMonitor-$variant.zip") -Force
}
finally { Pop-Location }
