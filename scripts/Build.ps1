#Requires -Version 5.1
param([switch] $Test)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerifiedDownload.ps1')

try {
    $root = Split-Path -Parent $PSScriptRoot
    Set-Location -LiteralPath $root
    if (![Environment]::Is64BitOperatingSystem) {
        throw 'Windows x64 is required.'
    }
    if (!(Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
        throw 'Install the .NET 10 x64 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun build.cmd.'
    }
    & dotnet.exe --version
    if ($LASTEXITCODE -ne 0) {
        throw 'A compatible .NET 10 SDK was not found. See global.json and install the .NET 10 x64 SDK.'
    }
    & dotnet.exe publish .\src\ImageCpuDemo\ImageCpuDemo.csproj --configuration Release --output .\bin --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed (dotnet exit $LASTEXITCODE)."
    }

    $lock = Get-Content -LiteralPath .\assets.lock.json -Raw | ConvertFrom-Json
    $archive = Join-Path $root ".cache\$($lock.backend.file)"
    Get-VerifiedAsset -Asset $lock.backend -Destination $archive
    $backendDirectory = Join-Path $root "backend\$($lock.backend.directory)"
    New-Item -ItemType Directory -Path $backendDirectory -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -ne $entry.Name -or !$entry.Name) {
                throw "Unexpected backend archive entry: $($entry.FullName)"
            }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $backendDirectory $entry.Name), $true)
        }
    } finally {
        $zip.Dispose()
    }
    $backend = Join-Path $backendDirectory 'sd-cli.exe'
    $version = & $backend --version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch 'commit 1b0ba10') {
        throw 'The pinned CPU backend did not start. Check that the Microsoft Visual C++ 2015-2022 x64 runtime is installed.'
    }
    Write-Host $version
    & $backend --list-devices
    if ($LASTEXITCODE -ne 0) {
        throw "CPU backend device check failed (exit $LASTEXITCODE)."
    }
    if ($Test) {
        & dotnet.exe run --project .\tests\ImageCpuDemo.Tests\ImageCpuDemo.Tests.csproj --configuration Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed (exit $LASTEXITCODE)."
        }
    }
    Write-Host "`nBuilt: $(Join-Path $root 'bin\ImageCpuDemo.exe')"
    Write-Host 'Models are not downloaded by build.cmd. Run prepare-models.cmd once, then bin\ImageCpuDemo.exe.'
    exit 0
} catch {
    [Console]::Error.WriteLine("BUILD ERROR: $($_.Exception.Message)")
    exit 1
}
