[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear
)

$ErrorActionPreference = 'Stop'
$stagingBundle = $null

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $repositoryRoot = Get-CadMaxRepositoryRoot
    $powerShellExecutable = Get-CadMaxPowerShellExecutable
    $buildScript = Join-Path $PSScriptRoot 'Build-AutoCADAdapter.ps1'
    $buildOutput = @(& $powerShellExecutable `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $buildScript `
        -AutoCADYear $AutoCADYear 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new(
            'AUTOCAD_ADAPTER_CURRENT_BUILD_REQUIRED')
    }

    $buildRoot = Join-Path $repositoryRoot ".tools\autocad\build\$AutoCADYear"
    $adapterAssembly = Join-Path $buildRoot 'CadMax.AutoCAD.Adapter.dll'
    if (-not (Test-Path -LiteralPath $adapterAssembly -PathType Leaf)) {
        throw [InvalidOperationException]::new('AUTOCAD_ADAPTER_OUTPUT_MISSING')
    }

    $autodeskBinaries = @(Get-ChildItem -LiteralPath $buildRoot -File -Recurse |
        Where-Object { $_.Name -iin @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll') })
    if ($autodeskBinaries.Count -ne 0) {
        throw [InvalidOperationException]::new('AUTODESK_BINARY_DETECTED')
    }

    $bundleOutputRoot = Join-Path $repositoryRoot '.tools\autocad\bundles'
    [IO.Directory]::CreateDirectory($bundleOutputRoot) | Out-Null
    $targetBundle = Get-CadMaxDevBundlePath -AutoCADYear $AutoCADYear
    $stagingBundle = Join-Path $bundleOutputRoot (
        ".staging-CAD-MAX-$AutoCADYear-$([Guid]::NewGuid().ToString('N'))")
    $backupBundle = "$targetBundle.previous"
    $contentsRoot = Join-Path $stagingBundle 'Contents\Windows'
    [IO.Directory]::CreateDirectory($contentsRoot) | Out-Null

    foreach ($assembly in Get-ChildItem -LiteralPath $buildRoot -Filter 'CadMax.*.dll' -File) {
        Copy-Item -LiteralPath $assembly.FullName -Destination $contentsRoot
    }
    $adapterDeps = Join-Path $buildRoot 'CadMax.AutoCAD.Adapter.deps.json'
    if (Test-Path -LiteralPath $adapterDeps -PathType Leaf) {
        Copy-Item -LiteralPath $adapterDeps -Destination $contentsRoot
    }

    $templatePath = Join-Path $repositoryRoot `
        'packaging\autocad\CAD-MAX.bundle\PackageContents.xml.template'
    $manifest = (Get-Content -LiteralPath $templatePath -Raw).
        Replace('{{APP_VERSION}}', (Get-CadMaxProjectVersion)).
        Replace('{{AUTOCAD_SERIES}}', (
            Get-CadMaxAutoCADSeries -AutoCADYear $AutoCADYear))
    if ($manifest.Contains('{{')) {
        throw [InvalidOperationException]::new('BUNDLE_TEMPLATE_TOKEN_UNRESOLVED')
    }
    [IO.File]::WriteAllText(
        (Join-Path $stagingBundle 'PackageContents.xml'),
        $manifest,
        [Text.UTF8Encoding]::new($false))

    $validation = Assert-CadMaxDevBundle `
        -AutoCADYear $AutoCADYear `
        -BundlePath $stagingBundle
    if (Test-Path -LiteralPath $backupBundle) {
        Remove-Item -LiteralPath $backupBundle -Recurse -Force
    }
    if (Test-Path -LiteralPath $targetBundle) {
        Move-Item -LiteralPath $targetBundle -Destination $backupBundle
    }

    try {
        Move-Item -LiteralPath $stagingBundle -Destination $targetBundle
        $validation = Assert-CadMaxDevBundle `
            -AutoCADYear $AutoCADYear `
            -BundlePath $targetBundle
    }
    catch {
        if (Test-Path -LiteralPath $targetBundle) {
            Remove-Item -LiteralPath $targetBundle -Recurse -Force
        }
        if (Test-Path -LiteralPath $backupBundle) {
            Move-Item -LiteralPath $backupBundle -Destination $targetBundle
        }
        throw
    }

    # Cleanup cannot invalidate a successfully staged and validated target bundle.
    if (Test-Path -LiteralPath $backupBundle) {
        Remove-Item -LiteralPath $backupBundle -Recurse -Force
    }

    [pscustomobject]@{
        autoCADYear = $AutoCADYear
        appVersion = $validation.AppVersion
        series = $validation.Series
        moduleName = $validation.ModuleName
        autodeskBinaryCount = $validation.AutodeskBinaryCount
        output = 'GIT_IGNORED'
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
finally {
    if ($null -ne $stagingBundle -and
        (Test-Path -LiteralPath $stagingBundle -PathType Container)) {
        Remove-Item -LiteralPath $stagingBundle -Recurse -Force
    }
}
