[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear,

    [Parameter(Mandatory)]
    [switch]$ConfirmTarget
)

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    if (-not $ConfirmTarget) {
        throw [InvalidOperationException]::new('EXPLICIT_CONFIRMATION_REQUIRED')
    }
    if (@(Get-Process -Name 'acad' -ErrorAction SilentlyContinue).Count -ne 0) {
        throw [InvalidOperationException]::new('AUTOCAD_PROCESS_RUNNING')
    }

    $applicationPluginsRoot = Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) `
        'Autodesk\ApplicationPlugins'
    $destinationBundle = Join-Path $applicationPluginsRoot "CAD-MAX-$AutoCADYear.bundle"
    if (-not (Test-CadMaxPathWithin `
            -Candidate $destinationBundle `
            -Root $applicationPluginsRoot)) {
        throw [InvalidOperationException]::new('UNINSTALL_TARGET_UNSAFE')
    }

    if (-not (Test-Path -LiteralPath $destinationBundle -PathType Container)) {
        [pscustomobject]@{
            autoCADYear = $AutoCADYear
            removed = $false
            reason = 'NOT_INSTALLED'
            target = 'REDACTED'
        } | ConvertTo-Json
        exit 0
    }

    $null = Assert-CadMaxBundleIdentity -BundlePath $destinationBundle
    Remove-Item -LiteralPath $destinationBundle -Recurse -Force
    if (Test-Path -LiteralPath $destinationBundle) {
        throw [InvalidOperationException]::new('UNINSTALL_INCOMPLETE')
    }

    [pscustomobject]@{
        autoCADYear = $AutoCADYear
        removed = $true
        productCode = Get-CadMaxProductCode
        target = 'REDACTED'
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
