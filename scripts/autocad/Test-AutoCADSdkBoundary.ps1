[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear,

    [string]$PropsPath
)

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    if ([string]::IsNullOrWhiteSpace($PropsPath)) {
        $PropsPath = Join-Path (Get-CadMaxRepositoryRoot) `
            '.tools\autocad\CadMax.AutoCAD.Local.props'
    }

    $result = Assert-CadMaxAutoCADSdkBoundary `
        -AutoCADYear $AutoCADYear `
        -PropsPath $PropsPath
    [pscustomobject]@{
        autoCADYear = $result.AutoCADYear
        productVersion = $result.ProductVersion
        managedDllVersions = $result.ManagedDllVersions
        sdkBoundary = 'PASS'
        paths = 'REDACTED'
    } | ConvertTo-Json -Depth 4
}
catch {
    if (Get-Command Write-CadMaxSafeError -ErrorAction SilentlyContinue) {
        Write-CadMaxSafeError -Exception $_.Exception
    }
    else {
        [Console]::Error.WriteLine('ERROR / INTERNAL_ERROR')
    }
    exit 1
}
