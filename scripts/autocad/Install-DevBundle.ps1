[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear,

    [Parameter(Mandatory)]
    [switch]$ConfirmTarget
)

$ErrorActionPreference = 'Stop'
$stagingBundle = $null

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    if (-not $ConfirmTarget) {
        throw [InvalidOperationException]::new('EXPLICIT_CONFIRMATION_REQUIRED')
    }
    if (@(Get-Process -Name 'acad' -ErrorAction SilentlyContinue).Count -ne 0) {
        throw [InvalidOperationException]::new('AUTOCAD_PROCESS_RUNNING')
    }

    $sourceBundle = Get-CadMaxDevBundlePath -AutoCADYear $AutoCADYear
    $sourceValidation = Assert-CadMaxDevBundle `
        -AutoCADYear $AutoCADYear `
        -BundlePath $sourceBundle
    $applicationPluginsRoot = Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) `
        'Autodesk\ApplicationPlugins'
    [IO.Directory]::CreateDirectory($applicationPluginsRoot) | Out-Null
    $destinationBundle = Join-Path $applicationPluginsRoot "CAD-MAX-$AutoCADYear.bundle"
    $stagingBundle = Join-Path $applicationPluginsRoot (
        ".CAD-MAX-$AutoCADYear.installing-$([Guid]::NewGuid().ToString('N'))")
    $backupBundle = "$destinationBundle.rollback-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

    if (-not (Test-CadMaxPathWithin `
            -Candidate $destinationBundle `
            -Root $applicationPluginsRoot)) {
        throw [InvalidOperationException]::new('INSTALL_TARGET_UNSAFE')
    }
    if (Test-Path -LiteralPath $destinationBundle -PathType Container) {
        $null = Assert-CadMaxBundleIdentity -BundlePath $destinationBundle
    }

    Copy-Item -LiteralPath $sourceBundle -Destination $stagingBundle -Recurse
    $null = Assert-CadMaxDevBundle `
        -AutoCADYear $AutoCADYear `
        -BundlePath $stagingBundle `
        -AllowedRoot $applicationPluginsRoot
    if (Test-Path -LiteralPath $destinationBundle -PathType Container) {
        Move-Item -LiteralPath $destinationBundle -Destination $backupBundle
    }

    try {
        Move-Item -LiteralPath $stagingBundle -Destination $destinationBundle
        $null = Assert-CadMaxDevBundle `
            -AutoCADYear $AutoCADYear `
            -BundlePath $destinationBundle `
            -AllowedRoot $applicationPluginsRoot
    }
    catch {
        if (Test-Path -LiteralPath $destinationBundle -PathType Container) {
            Remove-Item -LiteralPath $destinationBundle -Recurse -Force
        }
        if (Test-Path -LiteralPath $backupBundle -PathType Container) {
            Move-Item -LiteralPath $backupBundle -Destination $destinationBundle
        }
        throw
    }

    [pscustomobject]@{
        autoCADYear = $AutoCADYear
        appVersion = $sourceValidation.AppVersion
        installScope = 'CURRENT_USER'
        previousVersionBackup = Test-Path -LiteralPath $backupBundle -PathType Container
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
finally {
    if ($null -ne $stagingBundle -and
        (Test-Path -LiteralPath $stagingBundle -PathType Container)) {
        Remove-Item -LiteralPath $stagingBundle -Recurse -Force
    }
}
