param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.3',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$RuntimeVersion = '10.0.13',
    [string]$OutputDirectory,
    [switch]$SkipUiChecks
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "artifacts/releases/v$Version" }
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $releaseRoot) { throw "Release folder already exists: $releaseRoot. Choose a fresh output folder." }

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE" }
}

function New-ReleaseArchive {
    param([string]$Directory,[string]$Archive,[string]$Prefix='')
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::Open($Archive,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force) {
            $relative=$file.FullName.Substring($Directory.Length+1).Replace('\','/')
            $entry=$zip.CreateEntry($Prefix+$relative,[IO.Compression.CompressionLevel]::Optimal)
            $input=[IO.File]::OpenRead($file.FullName)
            try {$output=$entry.Open();try {$input.CopyTo($output)}finally{$output.Dispose()}}finally{$input.Dispose()}
        }
    } finally {$zip.Dispose()}
}

Push-Location $projectRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
    $sourceFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $sourceFiles.Count -eq 0) { throw 'Packaging requires a Git working copy with source files.' }
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
    $appRoot = Join-Path $releaseRoot 'NothingOnWindows'
    Invoke-DotNet -Arguments @('run', '--project', 'tests/ProtocolChecks.csproj', '-c', 'Release', '--disable-build-servers')
    Invoke-DotNet -Arguments @('publish', 'NothingOnWindows.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--disable-build-servers', "-p:Version=$Version", "-p:ReleaseRuntimeVersion=$RuntimeVersion", '-p:PublishTrimmed=false', '-o', $appRoot)

    if (-not $SkipUiChecks) {
        $checkReport = Join-Path $releaseRoot 'validation/ui-smoke.json'
        $checkProcess = Start-Process -FilePath (Join-Path $appRoot 'NothingOnWindows.exe') -ArgumentList '--ui-smoke', ('"' + $checkReport + '"') -WindowStyle Hidden -Wait -PassThru
        if ($checkProcess.ExitCode -ne 0) { throw "UI checks failed. See $checkReport" }
        $result = Get-Content -LiteralPath $checkReport -Raw | ConvertFrom-Json
        if ($result.passed -lt 330 -or $result.headphoneWrites -ne $false) { throw 'UI report is incomplete or reported hardware writes.' }
        Write-Host "Passed $($result.passed) offline UI/window/tray checks."
    }

    $sourceRoot = Join-Path $releaseRoot 'source'
    New-Item -ItemType Directory -Path $sourceRoot | Out-Null
    foreach ($relative in $sourceFiles) {
        $source = Join-Path $projectRoot $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $destination = Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    $sourceZip = Join-Path $releaseRoot "NothingOnWindows-v$Version-source.zip"
    New-ReleaseArchive -Directory $sourceRoot -Archive $sourceZip
    Copy-Item -LiteralPath $sourceZip -Destination (Join-Path $appRoot 'Source.zip')
    foreach ($name in @('README.md', 'LICENSE', 'DEVICE_SUPPORT.md', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $appRoot
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $appRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $appRoot -Recurse
    $assetRoot = Join-Path $appRoot 'Assets'
    New-Item -ItemType Directory -Force -Path (Join-Path $assetRoot 'Devices') | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets/README.md') -Destination $assetRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets/headphones.png') -Destination $assetRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets/Devices/sources.json') -Destination (Join-Path $assetRoot 'Devices')

    $manifestFiles=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $appRoot -File -Recurse | Sort-Object FullName) {
        $relative=$file.FullName.Substring($appRoot.Length+1).Replace('\','/')
        $manifestFiles[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    @{Version=$Version;Platform='win-x64';Files=$manifestFiles} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $appRoot 'release-manifest.json') -Encoding utf8
    $appZip = Join-Path $releaseRoot "NothingOnWindows-v$Version-win-x64.zip"
    New-ReleaseArchive -Directory $appRoot -Archive $appZip -Prefix 'NothingOnWindows/'
    $sums = foreach ($archive in @($appZip, $sourceZip)) {
        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($archive))"
    }
    [IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $sums, [Text.Encoding]::ASCII)
    Write-Host "Release ready: $releaseRoot"
}
finally { Pop-Location }

