[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Verification command failed with exit code $LASTEXITCODE."
    }
}

$locationPushed = $false
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'Windows is required for the WPF build and file-sharing scenarios.'
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    Push-Location -LiteralPath $repoRoot
    $locationPushed = $true

    $dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $dotnetCommand) {
        $dotnetPath = $dotnetCommand.Source
    }
    else {
        $dotnetPath = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
            throw 'The .NET SDK is unavailable. Install the SDK specified in global.json.'
        }
    }

    $expectedSdk = (Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json).sdk.version
    $actualSdk = & $dotnetPath --version
    if ($LASTEXITCODE -ne 0 -or "$actualSdk".Trim() -ne $expectedSdk) {
        throw "The exact .NET SDK $expectedSdk is required."
    }
    Write-Host "Using .NET SDK $expectedSdk"

    $solution = 'UseConfig.slnx'
    $probe = 'prototypes/UseConfig.StorageProbe/UseConfig.StorageProbe.csproj'
    $contracts = 'tests/UseConfig.ContractChecks/UseConfig.ContractChecks.csproj'
    $source = 'https://api.nuget.org/v3/index.json'
    Invoke-Checked $dotnetPath @('restore', $solution, '--source', $source)
    Invoke-Checked $dotnetPath @('restore', $probe, '--source', $source, '--locked-mode')
    Invoke-Checked $dotnetPath @('restore', $contracts, '--source', $source)

    foreach ($configuration in @('Debug', 'Release')) {
        foreach ($target in @($solution, $probe, $contracts)) {
            Invoke-Checked $dotnetPath @('build', $target, '--no-restore', '-c', $configuration)
        }
    }
    foreach ($target in @($solution, $probe, $contracts)) {
        Invoke-Checked $dotnetPath @('format', $target, '--verify-no-changes', '--no-restore')
    }
    foreach ($configuration in @('Debug', 'Release')) {
        $probeExecutable = Join-Path $repoRoot "prototypes/UseConfig.StorageProbe/bin/$configuration/net10.0/UseConfig.StorageProbe.exe"
        Invoke-Checked $probeExecutable @()
        $contractExecutable = Join-Path $repoRoot "tests/UseConfig.ContractChecks/bin/$configuration/net10.0/UseConfig.ContractChecks.exe"
        Invoke-Checked $contractExecutable @()
    }
    Write-Host 'All verification checks passed.'
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}
