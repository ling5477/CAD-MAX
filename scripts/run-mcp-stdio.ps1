[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$uvCommand = Get-Command uv -ErrorAction SilentlyContinue
$uv = if ($null -ne $uvCommand) {
    $uvCommand.Source
}
elseif (Test-Path -LiteralPath (Join-Path $repositoryRoot '.tools\uv\bin\uv.exe')) {
    (Resolve-Path -LiteralPath (Join-Path $repositoryRoot '.tools\uv\bin\uv.exe')).Path
}
else {
    throw 'uv was not found.'
}

Push-Location $repositoryRoot
try {
    & $uv run cad-max-mcp serve --transport stdio
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
