[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Assert-ThrowsCode {
    param(
        [Parameter(Mandatory)]
        [string]$ExpectedCode,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    try {
        & $Action
        throw [InvalidOperationException]::new('EXPECTED_FAILURE_NOT_THROWN')
    }
    catch {
        if ($_.Exception.Message -ne $ExpectedCode) {
            throw [InvalidOperationException]::new('UNEXPECTED_ERROR_CODE')
        }
    }
}

function Write-TestProps {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$SdkRoot,

        [Parameter(Mandatory)]
        [string]$ExecutablePath
    )

    $content = @"
<Project><PropertyGroup>
<CadMaxAutoCADYear>2025</CadMaxAutoCADYear>
<CadMaxAutoCADSdkRoot>$([Security.SecurityElement]::Escape($SdkRoot))</CadMaxAutoCADSdkRoot>
<CadMaxAutoCADExecutable>$([Security.SecurityElement]::Escape($ExecutablePath))</CadMaxAutoCADExecutable>
</PropertyGroup></Project>
"@
    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
}

function Write-TestBundleFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Manifest
    )

    $contentsRoot = Join-Path $Path 'Contents\Windows'
    [IO.Directory]::CreateDirectory($contentsRoot) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $Path 'PackageContents.xml'),
        $Manifest,
        [Text.UTF8Encoding]::new($false))
    foreach ($fileName in @(
            'CadMax.AutoCAD.Adapter.deps.json',
            'CadMax.AutoCAD.Adapter.dll',
            'CadMax.AutoCAD.Plugin.dll',
            'CadMax.Bridge.Core.dll',
            'CadMax.Contracts.dll')) {
        [IO.File]::WriteAllBytes((Join-Path $contentsRoot $fileName), [byte[]]::new(0))
    }
}

$repositoryRoot = $null
$fixtureRoot = $null
$bundleFixtureRoot = $null
$testFailure = $null

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $repositoryRoot = Get-CadMaxRepositoryRoot
    $testId = [Guid]::NewGuid().ToString('N')
    $fixtureRoot = Join-Path $repositoryRoot ".tools\autocad\script-tests\$testId"
    $bundleFixtureRoot = Join-Path $repositoryRoot ".tools\autocad\bundles\script-tests-$testId"
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    [IO.Directory]::CreateDirectory($bundleFixtureRoot) | Out-Null

    Assert-ThrowsCode -ExpectedCode 'SDK_CONFIG_MISSING' -Action {
        Read-CadMaxAutoCADLocalProps -PropsPath (
            Join-Path $fixtureRoot 'missing.props') | Out-Null
    }

    $missingRootProps = Join-Path $fixtureRoot 'missing-root.props'
    $missingRoot = "Z:\cad-max-missing-$testId"
    Write-TestProps `
        -Path $missingRootProps `
        -SdkRoot $missingRoot `
        -ExecutablePath ([IO.Path]::Combine($missingRoot, 'acad.exe'))
    Assert-ThrowsCode -ExpectedCode 'SDK_ROOT_MISSING' -Action {
        Assert-CadMaxAutoCADSdkBoundary `
            -AutoCADYear 2025 `
            -PropsPath $missingRootProps | Out-Null
    }

    $insideProps = Join-Path $fixtureRoot 'inside.props'
    Write-TestProps `
        -Path $insideProps `
        -SdkRoot $fixtureRoot `
        -ExecutablePath (Join-Path $fixtureRoot 'acad.exe')
    Assert-ThrowsCode -ExpectedCode 'SDK_ROOT_INSIDE_REPOSITORY' -Action {
        Assert-CadMaxAutoCADSdkBoundary `
            -AutoCADYear 2025 `
            -PropsPath $insideProps | Out-Null
    }

    $missingDllRoot = Join-Path $fixtureRoot 'missing-dll'
    [IO.Directory]::CreateDirectory($missingDllRoot) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $missingDllRoot 'AcMgd.dll'), [byte[]]::new(0))
    [IO.File]::WriteAllBytes((Join-Path $missingDllRoot 'AcDbMgd.dll'), [byte[]]::new(0))
    Assert-ThrowsCode -ExpectedCode 'AUTOCAD_MANAGED_DLL_MISSING' -Action {
        Get-CadMaxManagedDllVersions `
            -AutoCADYear 2025 `
            -SdkRoot $missingDllRoot | Out-Null
    }

    $mismatchDllRoot = Join-Path $fixtureRoot 'mismatch-dll'
    [IO.Directory]::CreateDirectory($mismatchDllRoot) | Out-Null
    foreach ($dllName in @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll')) {
        [IO.File]::WriteAllBytes((Join-Path $mismatchDllRoot $dllName), [byte[]]::new(0))
    }
    Assert-ThrowsCode -ExpectedCode 'AUTOCAD_MANAGED_DLL_VERSION_MISMATCH' -Action {
        Get-CadMaxManagedDllVersions `
            -AutoCADYear 2025 `
            -SdkRoot $mismatchDllRoot | Out-Null
    }

    $template = Get-Content -LiteralPath (
        Join-Path $repositoryRoot `
            'packaging\autocad\CAD-MAX.bundle\PackageContents.xml.template') -Raw
    $validManifest = $template.
        Replace('{{APP_VERSION}}', (Get-CadMaxProjectVersion)).
        Replace('{{AUTOCAD_SERIES}}', 'R25.0')

    $validBundle = Join-Path $bundleFixtureRoot 'valid'
    Write-TestBundleFixture -Path $validBundle -Manifest $validManifest
    Assert-CadMaxDevBundle -AutoCADYear 2025 -BundlePath $validBundle | Out-Null

    [IO.File]::WriteAllText(
        (Join-Path $validBundle 'unexpected.txt'),
        'fixture',
        [Text.UTF8Encoding]::new($false))
    Assert-ThrowsCode -ExpectedCode 'BUNDLE_FILE_NOT_ALLOWED' -Action {
        Assert-CadMaxDevBundle -AutoCADYear 2025 -BundlePath $validBundle | Out-Null
    }

    [xml]$duplicateComponentManifest = $validManifest
    $componentNode = $duplicateComponentManifest.SelectSingleNode(
        '/ApplicationPackage/Components/ComponentEntry')
    $componentClone = $componentNode.CloneNode($true)
    $componentNode.ParentNode.AppendChild($componentClone) | Out-Null
    $duplicateComponentBundle = Join-Path $bundleFixtureRoot 'duplicate-component'
    Write-TestBundleFixture `
        -Path $duplicateComponentBundle `
        -Manifest $duplicateComponentManifest.OuterXml
    Assert-ThrowsCode -ExpectedCode 'BUNDLE_COMPONENT_INVALID' -Action {
        Assert-CadMaxDevBundle `
            -AutoCADYear 2025 `
            -BundlePath $duplicateComponentBundle | Out-Null
    }

    [xml]$duplicateCommandManifest = $validManifest
    $commandNode = $duplicateCommandManifest.SelectSingleNode(
        '/ApplicationPackage/Components/ComponentEntry/Commands/Command')
    $commandClone = $commandNode.CloneNode($true)
    $commandNode.ParentNode.AppendChild($commandClone) | Out-Null
    $duplicateCommandBundle = Join-Path $bundleFixtureRoot 'duplicate-command'
    Write-TestBundleFixture `
        -Path $duplicateCommandBundle `
        -Manifest $duplicateCommandManifest.OuterXml
    Assert-ThrowsCode -ExpectedCode 'BUNDLE_STATUS_COMMAND_INVALID' -Action {
        Assert-CadMaxDevBundle `
            -AutoCADYear 2025 `
            -BundlePath $duplicateCommandBundle | Out-Null
    }

    $malformedBundle = Join-Path $bundleFixtureRoot 'malformed'
    [IO.Directory]::CreateDirectory($malformedBundle) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $malformedBundle 'PackageContents.xml'),
        '<ApplicationPackage>',
        [Text.UTF8Encoding]::new($false))
    Assert-ThrowsCode -ExpectedCode 'BUNDLE_MANIFEST_INVALID_XML' -Action {
        Assert-CadMaxDevBundle -AutoCADYear 2025 -BundlePath $malformedBundle | Out-Null
    }

    $absoluteBundle = Join-Path $bundleFixtureRoot 'absolute-module'
    [IO.Directory]::CreateDirectory($absoluteBundle) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $absoluteBundle 'PackageContents.xml'),
        $validManifest.Replace(
            './Contents/Windows/CadMax.AutoCAD.Adapter.dll',
            'C:\unsafe\CadMax.AutoCAD.Adapter.dll'),
        [Text.UTF8Encoding]::new($false))
    Assert-ThrowsCode -ExpectedCode 'BUNDLE_MANIFEST_ABSOLUTE_PATH' -Action {
        Assert-CadMaxDevBundle -AutoCADYear 2025 -BundlePath $absoluteBundle | Out-Null
    }

    $binaryBundle = Join-Path $bundleFixtureRoot 'autodesk-binary'
    $binaryContents = Join-Path $binaryBundle 'Contents\Windows'
    [IO.Directory]::CreateDirectory($binaryContents) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $binaryBundle 'PackageContents.xml'),
        $validManifest,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes(
        (Join-Path $binaryContents 'CadMax.AutoCAD.Adapter.dll'),
        [byte[]]::new(0))
    [IO.File]::WriteAllBytes(
        (Join-Path $binaryContents 'AcMgd.dll'),
        [byte[]]::new(0))
    Assert-ThrowsCode -ExpectedCode 'AUTODESK_BINARY_DETECTED' -Action {
        Assert-CadMaxDevBundle -AutoCADYear 2025 -BundlePath $binaryBundle | Out-Null
    }

    $foreignBundle = Join-Path $bundleFixtureRoot 'foreign'
    [IO.Directory]::CreateDirectory($foreignBundle) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $foreignBundle 'PackageContents.xml'),
        $validManifest.Replace((Get-CadMaxProductCode), [Guid]::NewGuid().ToString('B')),
        [Text.UTF8Encoding]::new($false))
    Assert-ThrowsCode -ExpectedCode 'BUNDLE_IDENTITY_MISMATCH' -Action {
        Assert-CadMaxBundleIdentity -BundlePath $foreignBundle | Out-Null
    }

    Assert-ThrowsCode -ExpectedCode 'WILDCARD_INPUT_REJECTED' -Action {
        Assert-CadMaxNoWildcard -Value '*'
    }

    & git -C $repositoryRoot check-ignore --quiet `
        '.tools/autocad/bundles/CAD-MAX-2025.bundle'
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('GENERATED_BUNDLE_NOT_IGNORED')
    }

    & git -C $repositoryRoot check-ignore --quiet `
        '.tools/autocad/CadMax.AutoCAD.Local.props'
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('LOCAL_SDK_PROPS_NOT_IGNORED')
    }

    $safeDetectionOutput = (& (Join-Path $PSScriptRoot 'Get-AutoCADInstallations.ps1') |
        Out-String)
    if ($safeDetectionOutput.Contains($repositoryRoot) -or
        $safeDetectionOutput -match '(?i)(?:[A-Z]:[\\/]|\\\\)') {
        throw [InvalidOperationException]::new('SCRIPT_OUTPUT_PATH_DISCLOSURE')
    }

    $childStandardOutput = Join-Path $fixtureRoot 'entry-script.stdout.log'
    $childStandardError = Join-Path $fixtureRoot 'entry-script.stderr.log'
    $missingChildProps = Join-Path $fixtureRoot 'child-missing.props'
    $powerShellExecutable = Get-CadMaxPowerShellExecutable
    & $powerShellExecutable `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'Test-AutoCADSdkBoundary.ps1') `
        -AutoCADYear 2025 `
        -PropsPath $missingChildProps `
        1> $childStandardOutput `
        2> $childStandardError
    if ($LASTEXITCODE -eq 0) {
        throw [InvalidOperationException]::new('ENTRY_SCRIPT_FAILURE_NOT_OBSERVED')
    }

    $childOutput = [IO.File]::ReadAllText($childStandardOutput) +
        [IO.File]::ReadAllText($childStandardError)
    if ($childOutput.Contains($repositoryRoot) -or
        $childOutput -match '(?i)(?:[A-Z]:[\\/]|\\\\)') {
        throw [InvalidOperationException]::new('SCRIPT_OUTPUT_PATH_DISCLOSURE')
    }

}
catch {
    $testFailure = $_.Exception
}

foreach ($cleanupRoot in @($fixtureRoot, $bundleFixtureRoot)) {
    try {
        if ($null -ne $cleanupRoot -and (Test-Path -LiteralPath $cleanupRoot)) {
            if ($null -eq $repositoryRoot -or
                -not (Test-CadMaxPathWithin `
                    -Candidate $cleanupRoot `
                    -Root (Join-Path $repositoryRoot '.tools\autocad'))) {
                throw [InvalidOperationException]::new('SCRIPT_TEST_CLEANUP_FAILED')
            }

            Remove-Item -LiteralPath $cleanupRoot -Recurse -Force -ErrorAction Stop
        }
        if ($null -ne $cleanupRoot -and (Test-Path -LiteralPath $cleanupRoot)) {
            throw [InvalidOperationException]::new('SCRIPT_TEST_CLEANUP_FAILED')
        }
    }
    catch {
        $testFailure = [InvalidOperationException]::new('SCRIPT_TEST_CLEANUP_FAILED')
    }
}

if ($null -ne $testFailure) {
    if (Get-Command Write-CadMaxSafeError -ErrorAction SilentlyContinue) {
        Write-CadMaxSafeError -Exception $testFailure
    }
    else {
        [Console]::Error.WriteLine('ERROR / INTERNAL_ERROR')
    }
    exit 1
}

[pscustomobject]@{
    result = 'PASS'
    cases = 18
    fixtures = 'EMPTY_PLACEHOLDERS_IN_GIT_IGNORED_PATH'
    paths = 'REDACTED'
} | ConvertTo-Json

# Expected fail-closed child cases leave LASTEXITCODE non-zero; all assertions and cleanup succeeded here.
exit 0
