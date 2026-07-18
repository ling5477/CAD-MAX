[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $installation = Get-CadMaxAutoCADInstallations |
        Where-Object { $_.Year -eq $AutoCADYear } |
        Select-Object -First 1
    if ($null -eq $installation) {
        throw [InvalidOperationException]::new('AUTOCAD_INSTALLATION_REQUIRED')
    }

    $propsRoot = Join-Path (Get-CadMaxRepositoryRoot) '.tools\autocad'
    $propsPath = Join-Path $propsRoot 'CadMax.AutoCAD.Local.props'
    if ((Test-Path -LiteralPath $propsPath -PathType Leaf) -and -not $Force) {
        throw [InvalidOperationException]::new('LOCAL_SDK_PROPS_EXISTS')
    }

    [IO.Directory]::CreateDirectory($propsRoot) | Out-Null
    $escapedRoot = [Security.SecurityElement]::Escape($installation.SdkRoot)
    $escapedExecutable = [Security.SecurityElement]::Escape($installation.ExecutablePath)
    $content = @"
<Project>
  <!-- Machine-local, Git-ignored, generated from a detected local AutoCAD installation. -->
  <PropertyGroup>
    <CadMaxAutoCADYear>$AutoCADYear</CadMaxAutoCADYear>
    <CadMaxAutoCADSdkRoot>$escapedRoot</CadMaxAutoCADSdkRoot>
    <CadMaxAutoCADExecutable>$escapedExecutable</CadMaxAutoCADExecutable>
  </PropertyGroup>
</Project>
"@
    [IO.File]::WriteAllText($propsPath, $content, [Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        autoCADYear = $AutoCADYear
        productVersion = $installation.ProductVersion
        props = 'CREATED_IN_GIT_IGNORED_PATH'
        sdkPath = 'REDACTED'
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
