#Requires -Version 5.1
param(
    [string] $ModelsDirectory,
    [switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerifiedDownload.ps1')

try {
    $root = Split-Path -Parent $PSScriptRoot
    if (!$ModelsDirectory) {
        $ModelsDirectory = Join-Path $root 'models'
    }
    $ModelsDirectory = [IO.Path]::GetFullPath($ModelsDirectory)
    $lock = Get-Content -LiteralPath (Join-Path $root 'assets.lock.json') -Raw | ConvertFrom-Json
    $total = ($lock.models | Measure-Object -Property size -Sum).Sum
    Write-Host ("Qwen-Image-2.1-Turbo: {0:N2} GiB in 3 pinned model files." -f ($total / 1GB))
    Write-Host 'Qwen Research License: research/evaluation only; commercial use requires a separate license.'
    Write-Host 'See MODEL-LICENSES.md and licenses\Qwen-Research-LICENSE.txt before using or redistributing the model.'
    foreach ($model in $lock.models) {
        Get-VerifiedAsset -Asset $model -Destination (Join-Path $ModelsDirectory $model.file) -VerifyOnly:$VerifyOnly
    }
    Write-Host "All model sizes and SHA256 hashes verified: $ModelsDirectory"
    exit 0
} catch {
    [Console]::Error.WriteLine("ERROR: $($_.Exception.Message)")
    exit 1
}
