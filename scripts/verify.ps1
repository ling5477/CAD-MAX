[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.tools\nuget-packages'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

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
        throw 'uv was not found.'
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnet = if ($null -ne $dotnetCommand -and (& $dotnetCommand.Source --list-sdks)) {
        $dotnetCommand.Source
    }
    elseif (Test-Path -LiteralPath '.tools\dotnet\dotnet.exe') {
        (Resolve-Path -LiteralPath '.tools\dotnet\dotnet.exe').Path
    }
    else {
        throw '.NET 8 SDK was not found.'
    }

    Invoke-Checked 'uv sync --frozen' { & $uv sync --frozen }
    Invoke-Checked 'ruff check' { & $uv run ruff check . }
    Invoke-Checked 'ruff format check' { & $uv run ruff format --check . }
    Invoke-Checked 'mypy' { & $uv run mypy src/python }
    Invoke-Checked 'pytest' { & $uv run pytest }
    Invoke-Checked 'doctor smoke' { & $uv run cad-max-mcp doctor }
    Invoke-Checked 'dotnet restore' {
        & $dotnet restore 'src\dotnet\CadMax.sln' --locked-mode --configfile 'NuGet.Config'
    }
    Invoke-Checked 'dotnet build' {
        & $dotnet build 'src\dotnet\CadMax.sln' --configuration Release --no-restore
    }
    Invoke-Checked 'dotnet test' {
        & $dotnet test 'src\dotnet\CadMax.sln' --configuration Release --no-build
    }
}
finally {
    Pop-Location
}
