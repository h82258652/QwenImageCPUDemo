#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-AssetHash {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [long] $Size,
        [Parameter(Mandatory)] [string] $Sha256
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing file: $Path"
    }
    $actualSize = (Get-Item -LiteralPath $Path).Length
    if ($actualSize -ne $Size) {
        throw "Size mismatch for '$Path': expected $Size bytes, got $actualSize. No unverified file will be used."
    }
    Write-Host "Checking SHA256: $Path"
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $actualHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
    if ($actualHash -ne $Sha256) {
        throw "SHA256 mismatch for '$Path'. Expected $Sha256, got $actualHash. Move or remove this specific file before retrying."
    }
}

function Get-VerifiedAsset {
    param(
        [Parameter(Mandatory)] $Asset,
        [Parameter(Mandatory)] [string] $Destination,
        [switch] $VerifyOnly
    )

    if (Test-Path -LiteralPath $Destination) {
        Assert-AssetHash -Path $Destination -Size $Asset.size -Sha256 $Asset.sha256
        return
    }
    if ($VerifyOnly) {
        throw "Missing file: $Destination. Run prepare-models.cmd to download the models."
    }
    if (!(Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw 'curl.exe is required. Use the curl included with Windows 10/11, then retry.'
    }
    if (([uri]$Asset.url).Scheme -ne 'https') {
        throw "Only HTTPS downloads are permitted: $($Asset.url)"
    }

    $directory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $partial = "$Destination.part"
    if ((Test-Path -LiteralPath $partial) -and (Get-Item -LiteralPath $partial).Length -gt $Asset.size) {
        throw "Partial file is too large: $partial. Move or remove this specific file before retrying."
    }

    Write-Host ("Downloading {0} ({1:N2} GiB); interrupted downloads resume on the next run." -f $Asset.file, ($Asset.size / 1GB))
    if (!(Test-Path -LiteralPath $partial) -or (Get-Item -LiteralPath $partial).Length -lt $Asset.size) {
        & curl.exe --fail --location --silent --show-error --proto '=https' --proto-redir '=https' `
            --retry 4 --retry-delay 3 --connect-timeout 30 --speed-time 120 --speed-limit 1024 `
            --continue-at - --output $partial $Asset.url
        if ($LASTEXITCODE -ne 0) {
            throw "Download failed (curl exit $LASTEXITCODE): $($Asset.url). Partial data is retained at '$partial'; rerun to resume."
        }
    }

    Assert-AssetHash -Path $partial -Size $Asset.size -Sha256 $Asset.sha256
    Move-Item -LiteralPath $partial -Destination $Destination
    Write-Host "Ready: $Destination"
}
