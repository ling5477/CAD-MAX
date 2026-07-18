[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear,

    [string]$BundlePath
)

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    if ([string]::IsNullOrWhiteSpace($BundlePath)) {
        $BundlePath = Get-CadMaxDevBundlePath -AutoCADYear $AutoCADYear
    }

    $validation = Assert-CadMaxDevBundle `
        -AutoCADYear $AutoCADYear `
        -BundlePath $BundlePath
    [pscustomobject]@{
        autoCADYear = $AutoCADYear
        appVersion = $validation.AppVersion
        series = $validation.Series
        moduleName = $validation.ModuleName
        manifest = 'VALID'
        autodeskBinaryCount = $validation.AutodeskBinaryCount
        paths = 'REDACTED'
    } | ConvertTo-Json
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
