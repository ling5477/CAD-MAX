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
    $repositoryRoot = Get-CadMaxRepositoryRoot
    if ([string]::IsNullOrWhiteSpace($PropsPath)) {
        $PropsPath = Join-Path $repositoryRoot '.tools\autocad\CadMax.AutoCAD.Local.props'
    }

    $boundary = Assert-CadMaxAutoCADSdkBoundary `
        -AutoCADYear $AutoCADYear `
        -PropsPath $PropsPath
    $dotnet = Get-CadMaxDotNetExecutable
    $project = Join-Path $repositoryRoot `
        'src\dotnet\CadMax.AutoCAD.Adapter\CadMax.AutoCAD.Adapter.csproj'
    $outputRoot = Join-Path $repositoryRoot ".tools\autocad\build\$AutoCADYear"
    if (Test-Path -LiteralPath $outputRoot -PathType Container) {
        if (-not (Test-CadMaxPathWithin `
                -Candidate $outputRoot `
                -Root (Join-Path $repositoryRoot '.tools\autocad\build'))) {
            throw [InvalidOperationException]::new('BUILD_OUTPUT_LOCATION_UNSAFE')
        }
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null

    $env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $repositoryRoot '.tools\nuget-packages'
    $restoreOutput = @(& $dotnet restore $project `
        --locked-mode `
        --verbosity quiet `
        --configfile (Join-Path $repositoryRoot 'NuGet.Config') `
        "-p:CadMaxAutoCADLocalProps=$PropsPath" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $restoreCodes = [regex]::Matches(
            ($restoreOutput -join "`n"),
            '\b(?:NU|MSB)[0-9]{4}\b') |
            ForEach-Object { $_.Value } |
            Sort-Object -Unique
        [Console]::Error.WriteLine(
            "RESTORE_ERROR_CODES / $($restoreCodes -join ',')")
        throw [InvalidOperationException]::new('AUTOCAD_ADAPTER_RESTORE_FAILED')
    }

    $buildOutput = @(& $dotnet build $project `
        --configuration Release `
        --no-restore `
        --warnaserror `
        --verbosity quiet `
        --output $outputRoot `
        "-p:CadMaxAutoCADLocalProps=$PropsPath" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $buildCodes = [regex]::Matches(
            ($buildOutput -join "`n"),
            '\b(?:CS|CA|MSB)[0-9]{4}\b') |
            ForEach-Object { $_.Value } |
            Sort-Object -Unique
        [Console]::Error.WriteLine(
            "BUILD_ERROR_CODES / $($buildCodes -join ',')")
        throw [InvalidOperationException]::new('AUTOCAD_ADAPTER_BUILD_FAILED')
    }

    $adapterAssembly = Join-Path $outputRoot 'CadMax.AutoCAD.Adapter.dll'
    if (-not (Test-Path -LiteralPath $adapterAssembly -PathType Leaf)) {
        throw [InvalidOperationException]::new('AUTOCAD_ADAPTER_OUTPUT_MISSING')
    }

    $autodeskBinaries = @(Get-ChildItem -LiteralPath $outputRoot -File -Recurse |
        Where-Object { $_.Name -iin @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll') })
    if ($autodeskBinaries.Count -ne 0) {
        throw [InvalidOperationException]::new('AUTODESK_BINARY_DETECTED')
    }

    [pscustomobject]@{
        autoCADYear = $AutoCADYear
        productVersion = $boundary.ProductVersion
        adapterVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
            $adapterAssembly).FileVersion
        output = 'GIT_IGNORED'
        autodeskBinaryCount = 0
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
