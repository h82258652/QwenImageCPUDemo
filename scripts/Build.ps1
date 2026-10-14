#Requires -Version 5.1
param([switch] $Test, [switch] $CpuOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

    & (Join-Path $PSScriptRoot 'PrepareBackends.ps1') -CpuOnly:$CpuOnly
    if ($Test) {
        & dotnet.exe run --project .\tests\ImageCpuDemo.Tests\ImageCpuDemo.Tests.csproj --configuration Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed (exit $LASTEXITCODE)."
        }
    }
    Write-Host "`nBuilt: $(Join-Path $root 'bin\ImageCpuDemo.exe')"
    Write-Host 'Models are not downloaded by build.cmd. Run prepare-models.cmd once, then open bin\ImageCpuDemo.exe for the WinForms UI.'
    exit 0
} catch {
    [Console]::Error.WriteLine("BUILD ERROR: $($_.Exception.Message)")
    exit 1
}
