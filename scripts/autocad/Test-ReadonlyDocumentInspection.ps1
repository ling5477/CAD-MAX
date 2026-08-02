<#
.SYNOPSIS
运行 Phase 1.4 SDK-free contract、listener、queue、redaction 与只读源码边界回归。
.DESCRIPTION
在测试前强制从当前源重新构建；递归扫描 production Adapter/Plugin C# 源码，并用 Git 忽略的
known-bad fixture 回归扫描器本身。不会启动 AutoCAD 或访问 DWG。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$uv = Join-Path $repositoryRoot '.tools\uv\bin\uv.exe'
$fixtureRoot = $null
$forbiddenPatterns = [ordered]@{
    'OpenMode.ForWrite' = '\bOpenMode\.ForWrite\b'
    'UpgradeOpen' = '\bUpgradeOpen\b'
    'DowngradeOpen' = '\bDowngradeOpen\b'
    'AddNewlyCreatedDBObject' = '\bAddNewlyCreatedDBObject\b'
    'Save' = '\bSave\s*\('
    'SaveAs' = '\bSaveAs\s*\('
    'CloseAndSave' = '\bCloseAndSave\s*\('
    'UpdateExt' = '\bUpdateExt\s*\('
    'Regen' = '\bRegen\s*\('
    'ZoomExtents' = '\bZoomExtents\s*\('
    'Purge' = '\bPurge\s*\('
    'Recover' = '\bRecover\s*\('
    'SendStringToExecute' = '\bSendStringToExecute\s*\('
    'SendCommand' = '\bSendCommand\s*\('
    'DocumentLock' = '\bDocumentLock\b'
    'TransactionCommit' = '\bCommit\s*\('
    'DBObjectErase' = '\bErase\s*\('
    'BlockTableRecord' = '\bBlockTableRecord\b'
    'LayerTable' = '\bLayerTable\b'
    'BlockTable' = '\bBlockTable\b'
    'TextStyleTable' = '\bTextStyleTable\b'
    'DimStyleTable' = '\bDimStyleTable\b'
    'LinetypeTable' = '\bLinetypeTable\b'
    'SelectionSet' = '\bSelectionSet\b'
    'PickFirst' = '\bPickFirst\b'
    'Handle' = '\bHandle\b'
    'ObjectId' = '\bObjectId\b'
}

function Assert-CadMaxReadOnlySource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]]$SourceRoots
    )

    $productionFiles = @(
        foreach ($sourceRoot in $SourceRoots) {
            if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
                throw [InvalidOperationException]::new('READONLY_SOURCE_ROOT_MISSING')
            }
            Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File
        }
    )
    if ($productionFiles.Count -eq 0) {
        throw [InvalidOperationException]::new('READONLY_SOURCE_FILES_MISSING')
    }

    foreach ($file in $productionFiles) {
        $source = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
            if ([regex]::IsMatch(
                    $source,
                    [string]$entry.Value,
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                throw [InvalidOperationException]::new(
                    "READONLY_SOURCE_BOUNDARY_FAILED token=$($entry.Key)")
            }
        }
    }
}

function Assert-ThrowsCode {
    [CmdletBinding()]
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
            throw [InvalidOperationException]::new('KNOWN_BAD_FIXTURE_REGRESSION_FAILED')
        }
    }
}

function Invoke-CadMaxSuppressedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$FileName,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory)]
        [string]$WorkingDirectory
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    foreach ($argument in $ArgumentList) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw [InvalidOperationException]::new('SUPPRESSED_PROCESS_START_FAILED')
    }

    try {
        # Native diagnostics can contain local paths. Drain both streams without
        # relaying or retaining their content, while preserving the child exit code.
        $standardOutputDrain = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
        $standardErrorDrain = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        $process.WaitForExit()
        [void]$standardOutputDrain.GetAwaiter().GetResult()
        [void]$standardErrorDrain.GetAwaiter().GetResult()
        return $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

Push-Location $repositoryRoot
try {
    if (-not (Test-Path -LiteralPath $uv -PathType Leaf)) {
        throw [InvalidOperationException]::new('UV_EXECUTABLE_MISSING')
    }

    $productionRoots = @(
        (Join-Path $repositoryRoot 'src\dotnet\CadMax.AutoCAD.Adapter'),
        (Join-Path $repositoryRoot 'src\dotnet\CadMax.AutoCAD.Plugin')
    )
    Assert-CadMaxReadOnlySource -SourceRoots $productionRoots

    $fixtureRoot = Join-Path $repositoryRoot (
        '.tools\autocad\readonly-inspection-tests\' + [Guid]::NewGuid().ToString('N'))
    $nestedFixtureDirectory = Join-Path $fixtureRoot 'Nested'
    [IO.Directory]::CreateDirectory($nestedFixtureDirectory) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $nestedFixtureDirectory 'Unsafe.cs'),
        'internal static class Unsafe { private static void Run() { var mode = OpenMode.ForWrite; } }',
        [Text.UTF8Encoding]::new($false))
    Assert-ThrowsCode -ExpectedCode 'READONLY_SOURCE_BOUNDARY_FAILED token=OpenMode.ForWrite' -Action {
        Assert-CadMaxReadOnlySource -SourceRoots @($fixtureRoot)
    }

    $buildExitCode = Invoke-CadMaxSuppressedProcess `
        -FileName 'dotnet' `
        -ArgumentList @(
            'build',
            'src\dotnet\CadMax.sln',
            '--configuration',
            'Release',
            '--no-restore',
            '--no-incremental') `
        -WorkingDirectory $repositoryRoot
    if ($buildExitCode -ne 0) {
        throw [InvalidOperationException]::new('DRAWING_CURRENT_SOURCE_BUILD_FAILED')
    }

    $contractTestsExitCode = Invoke-CadMaxSuppressedProcess `
        -FileName 'dotnet' `
        -ArgumentList @(
            'test',
            'src\dotnet\CadMax.Contracts.Tests\CadMax.Contracts.Tests.csproj',
            '--configuration',
            'Release',
            '--no-build',
            '--filter',
            'FullyQualifiedName~DrawingInspection') `
        -WorkingDirectory $repositoryRoot
    if ($contractTestsExitCode -ne 0) {
        throw [InvalidOperationException]::new('DRAWING_CONTRACT_TESTS_FAILED')
    }

    $pluginTestsExitCode = Invoke-CadMaxSuppressedProcess `
        -FileName 'dotnet' `
        -ArgumentList @(
            'test',
            'src\dotnet\CadMax.AutoCAD.Plugin.Tests\CadMax.AutoCAD.Plugin.Tests.csproj',
            '--configuration',
            'Release',
            '--no-build',
            '--filter',
            'FullyQualifiedName~DrawingInspection') `
        -WorkingDirectory $repositoryRoot
    if ($pluginTestsExitCode -ne 0) {
        throw [InvalidOperationException]::new('DRAWING_PLUGIN_TESTS_FAILED')
    }

    $pythonTestsExitCode = Invoke-CadMaxSuppressedProcess `
        -FileName $uv `
        -ArgumentList @(
            'run',
            'pytest',
            'tests/python/test_drawing_models.py',
            'tests/python/test_backends.py',
            'tests/python/test_cli.py',
            'tests/python/test_streamable_http_auth.py',
            '--basetemp',
            '.tools/pytest-readonly-inspection') `
        -WorkingDirectory $repositoryRoot
    if ($pythonTestsExitCode -ne 0) {
        throw [InvalidOperationException]::new('DRAWING_PYTHON_TESTS_FAILED')
    }

    [pscustomobject]@{
        result = 'PASS'
        mode = 'SDK_FREE'
        route = '/v1/drawing/inspect'
        operations = 7
        sourceBoundary = 'READ_ONLY'
        sourceScan = 'RECURSIVE_WITH_KNOWN_BAD_FIXTURE'
        buildFreshness = 'CURRENT_SOURCE_REBUILT'
        documentLock = 'ABSENT'
        forWrite = 'ABSENT'
        objectEnumeration = 'ABSENT'
        paths = 'REDACTED'
    } | ConvertTo-Json -Compress
    exit 0
}
catch {
    [Console]::Error.WriteLine('ERROR / READONLY_DOCUMENT_INSPECTION_TEST_FAILED')
    exit 1
}
finally {
    $cleanupFailed = $false
    try {
        if ($null -ne $fixtureRoot -and (Test-Path -LiteralPath $fixtureRoot)) {
            $fixtureParent = [IO.Path]::GetFullPath(
                (Join-Path $repositoryRoot '.tools\autocad\readonly-inspection-tests'))
            $candidate = [IO.Path]::GetFullPath($fixtureRoot)
            if (-not $candidate.StartsWith(
                    $fixtureParent + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw [InvalidOperationException]::new('READONLY_FIXTURE_CLEANUP_UNSAFE')
            }
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction Stop
        }
    }
    catch {
        $cleanupFailed = $true
    }
    Pop-Location
    if ($cleanupFailed) {
        [Console]::Error.WriteLine('ERROR / READONLY_FIXTURE_CLEANUP_FAILED')
        exit 1
    }
}
