#Requires -Version 5.1
param([switch] $CpuOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerifiedDownload.ps1')

$root = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $root 'assets.lock.json') -Raw | ConvertFrom-Json
$backends = @($lock.backend)
if (!$CpuOnly) {
    $backends += $lock.gpuBackend
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($asset in $backends) {
    $archive = Join-Path $root ".cache\$($asset.file)"
    Get-VerifiedAsset -Asset $asset -Destination $archive
    $directory = Join-Path $root "backend\$($asset.directory)"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -ne $entry.Name -or !$entry.Name) {
                throw "Unexpected backend archive entry: $($entry.FullName)"
            }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $directory $entry.Name), $true)
        }
    } finally {
        $zip.Dispose()
    }
    $version = & (Join-Path $directory 'sd-cli.exe') --version
    $commit = $asset.revision.Substring(0, 7)
    if ($LASTEXITCODE -ne 0 -or ($version -join "`n") -notmatch "commit $commit") {
        throw "The pinned backend '$($asset.directory)' did not start. Check the Microsoft Visual C++ x64 runtime and, for Vulkan, the graphics driver."
    }
    Write-Host "$($asset.directory): $version"
}
