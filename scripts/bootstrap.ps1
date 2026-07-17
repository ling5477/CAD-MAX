[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.tools\nuget-packages'
Push-Location $repositoryRoot
try {
    $uvCommand = Get-Command uv -ErrorAction SilentlyContinue
    $uv = if ($null -ne $uvCommand) {
        $uvCommand.Source
    }
    elseif (Test-Path -LiteralPath '.tools\uv\bin\uv.exe') {
        (Resolve-Path -LiteralPath '.tools\uv\bin\uv.exe').Path
    }
    else {
        throw 'uv was not found. Install uv before bootstrapping.'
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnet = if ($null -ne $dotnetCommand -and (& $dotnetCommand.Source --list-sdks)) {
        $dotnetCommand.Source
    }
    elseif (Test-Path -LiteralPath '.tools\dotnet\dotnet.exe') {
        (Resolve-Path -LiteralPath '.tools\dotnet\dotnet.exe').Path
    }
    else {
        throw '.NET 8 SDK was not found. Install it before bootstrapping.'
    }

    & $uv sync --frozen
    if ($LASTEXITCODE -ne 0) {
        throw 'uv sync failed.'
    }

    & $dotnet restore 'src\dotnet\CadMax.sln' --locked-mode --configfile 'NuGet.Config'
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet restore failed.'
    }
}
finally {
    Pop-Location
}
