[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $safeInstallations = Get-CadMaxAutoCADInstallations | ForEach-Object {
        [pscustomobject]@{
            autoCADYear = $_.Year
            productVersion = $_.ProductVersion
            managedDllVersions = $_.ManagedDllVersions
            net8Boundary = $_.Year -in @(2025, 2026)
            path = 'REDACTED'
        }
    }

    @($safeInstallations) | ConvertTo-Json -Depth 4
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
