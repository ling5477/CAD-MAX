Set-StrictMode -Version Latest

$script:CadMaxRepositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$script:CadMaxLocalPropsRoot = [IO.Path]::GetFullPath(
    (Join-Path $script:CadMaxRepositoryRoot '.tools\autocad'))
$script:CadMaxBundleOutputRoot = [IO.Path]::GetFullPath(
    (Join-Path $script:CadMaxRepositoryRoot '.tools\autocad\bundles'))
$script:CadMaxProductCode = '{D15A06D8-5024-4C70-B77D-8AA4B9A74B20}'
$script:CadMaxUpgradeCode = '{EDC7CB3B-DFCF-466F-9061-0DA05C669CF2}'

function Get-CadMaxRepositoryRoot {
    [CmdletBinding()]
    param()

    return $script:CadMaxRepositoryRoot
}

function Get-CadMaxCanonicalPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Test-CadMaxPathWithin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Candidate,

        [Parameter(Mandatory)]
        [string]$Root
    )

    $candidatePath = Get-CadMaxCanonicalPath -Path $Candidate
    $rootPath = Get-CadMaxCanonicalPath -Path $Root
    if ($candidatePath.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $rootWithSeparator = $rootPath + [IO.Path]::DirectorySeparatorChar
    return $candidatePath.StartsWith(
        $rootWithSeparator,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-CadMaxNoWildcard {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    if ([Management.Automation.WildcardPattern]::ContainsWildcardCharacters($Value)) {
        throw [InvalidOperationException]::new('WILDCARD_INPUT_REJECTED')
    }
}

function Get-CadMaxSafeErrorCode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Exception]$Exception
    )

    $candidate = [string]$Exception.Message
    if ($candidate -match '^[A-Z][A-Z0-9_]{0,127}$') {
        return $candidate
    }

    return 'INTERNAL_ERROR'
}

function Write-CadMaxSafeError {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Exception]$Exception
    )

    [Console]::Error.WriteLine(
        "ERROR / $(Get-CadMaxSafeErrorCode -Exception $Exception)")
}

function Get-CadMaxPowerShellExecutable {
    [CmdletBinding()]
    param()

    foreach ($candidateName in @('pwsh.exe', 'powershell.exe')) {
        $candidatePath = Join-Path $PSHOME $candidateName
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return $candidatePath
        }
    }

    throw [InvalidOperationException]::new('POWERSHELL_EXECUTABLE_MISSING')
}

function Get-CadMaxAutoCADSeries {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear
    )

    switch ($AutoCADYear) {
        2025 { 'R25.0' }
        2026 { 'R25.1' }
    }
}

function Get-CadMaxManagedVersionPrefix {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear
    )

    switch ($AutoCADYear) {
        2025 { '25.0.' }
        2026 { '25.1.' }
    }
}

function Test-CadMaxManagedVersionMatchesYear {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$FileVersion
    )

    return $FileVersion.StartsWith(
        (Get-CadMaxManagedVersionPrefix -AutoCADYear $AutoCADYear),
        [StringComparison]::Ordinal)
}

function Get-CadMaxManagedDllVersions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear,

        [Parameter(Mandatory)]
        [string]$SdkRoot
    )

    $dllNames = @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll')
    foreach ($dllName in $dllNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $SdkRoot $dllName) -PathType Leaf)) {
            throw [InvalidOperationException]::new('AUTOCAD_MANAGED_DLL_MISSING')
        }
    }

    $dllVersions = [ordered]@{}
    foreach ($dllName in $dllNames) {
        $dllPath = Join-Path $SdkRoot $dllName
        $fileVersion = [string][Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
        if (-not (Test-CadMaxManagedVersionMatchesYear `
                -AutoCADYear $AutoCADYear `
                -FileVersion $fileVersion)) {
            throw [InvalidOperationException]::new('AUTOCAD_MANAGED_DLL_VERSION_MISMATCH')
        }

        $dllVersions[$dllName] = $fileVersion
    }

    return [pscustomobject]$dllVersions
}

function Get-CadMaxAutoCADInstallations {
    [CmdletBinding()]
    param()

    $candidateExecutables = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $autodeskProgramFiles = Join-Path $env:ProgramFiles 'Autodesk'

    if (Test-Path -LiteralPath $autodeskProgramFiles -PathType Container) {
        Get-ChildItem -LiteralPath $autodeskProgramFiles -Filter 'acad.exe' -File -Recurse -Depth 3 |
            ForEach-Object {
                [void]$candidateExecutables.Add($_.FullName)
            }
    }

    $registryRoots = @(
        'HKLM:\SOFTWARE\Autodesk\AutoCAD',
        'HKLM:\SOFTWARE\WOW6432Node\Autodesk\AutoCAD'
    )
    foreach ($registryRoot in $registryRoots) {
        if (-not (Test-Path -LiteralPath $registryRoot)) {
            continue
        }

        foreach ($key in Get-ChildItem -LiteralPath $registryRoot -Recurse -ErrorAction SilentlyContinue) {
            $properties = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $properties) {
                continue
            }

            foreach ($property in $properties.PSObject.Properties) {
                if ($property.Value -isnot [string] -or
                    [string]::IsNullOrWhiteSpace($property.Value)) {
                    continue
                }

                $candidateValue = [Environment]::ExpandEnvironmentVariables($property.Value)
                try {
                    if (-not [IO.Path]::IsPathRooted($candidateValue) -or
                        $candidateValue.IndexOfAny([IO.Path]::GetInvalidPathChars()) -ge 0) {
                        continue
                    }

                    if (Test-Path -LiteralPath $candidateValue -PathType Leaf -ErrorAction SilentlyContinue) {
                        if ([IO.Path]::GetFileName($candidateValue) -ieq 'acad.exe') {
                            [void]$candidateExecutables.Add($candidateValue)
                        }
                    }
                    elseif (Test-Path -LiteralPath $candidateValue -PathType Container -ErrorAction SilentlyContinue) {
                        $candidateExecutable = Join-Path $candidateValue 'acad.exe'
                        if (Test-Path -LiteralPath $candidateExecutable -PathType Leaf -ErrorAction SilentlyContinue) {
                            [void]$candidateExecutables.Add($candidateExecutable)
                        }
                    }
                }
                catch {
                    # Registry values are untrusted strings; non-path values are ignored.
                    continue
                }
            }
        }
    }

    $installations = foreach ($executablePath in $candidateExecutables) {
        $sdkRoot = Split-Path -Parent $executablePath
        $executableInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
        $productVersion = [string]$executableInfo.ProductVersion
        $year = if ($productVersion -match '^R25\.0(?:\.|$)') {
            2025
        }
        elseif ($productVersion -match '^R25\.1(?:\.|$)') {
            2026
        }
        else {
            $null
        }

        if ($null -eq $year) {
            continue
        }

        $managedDllVersions = [ordered]@{}
        foreach ($dllName in @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll')) {
            $dllPath = Join-Path $sdkRoot $dllName
            $managedDllVersions[$dllName] = if (Test-Path -LiteralPath $dllPath -PathType Leaf) {
                [string][Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
            }
            else {
                'MISSING'
            }
        }

        [pscustomobject]@{
            Year = [int]$year
            ProductVersion = $productVersion
            SdkRoot = Get-CadMaxCanonicalPath -Path $sdkRoot
            ExecutablePath = Get-CadMaxCanonicalPath -Path $executablePath
            ManagedDllVersions = [pscustomobject]$managedDllVersions
        }
    }

    return @(
        $installations |
            Sort-Object Year, ProductVersion, SdkRoot, ExecutablePath -Unique
    )
}

function Read-CadMaxAutoCADLocalProps {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PropsPath
    )

    Assert-CadMaxNoWildcard -Value $PropsPath
    $canonicalPropsPath = Get-CadMaxCanonicalPath -Path $PropsPath
    if (-not (Test-CadMaxPathWithin -Candidate $canonicalPropsPath -Root $script:CadMaxLocalPropsRoot)) {
        throw [InvalidOperationException]::new('SDK_CONFIG_LOCATION_UNSAFE')
    }

    if (-not (Test-Path -LiteralPath $canonicalPropsPath -PathType Leaf)) {
        throw [InvalidOperationException]::new('SDK_CONFIG_MISSING')
    }

    try {
        [xml]$props = Get-Content -LiteralPath $canonicalPropsPath -Raw
    }
    catch {
        throw [InvalidOperationException]::new('SDK_CONFIG_INVALID')
    }

    $yearNode = $props.SelectSingleNode('/Project/PropertyGroup/CadMaxAutoCADYear')
    $rootNode = $props.SelectSingleNode('/Project/PropertyGroup/CadMaxAutoCADSdkRoot')
    $executableNode = $props.SelectSingleNode('/Project/PropertyGroup/CadMaxAutoCADExecutable')
    if ($null -eq $yearNode -or $null -eq $rootNode -or $null -eq $executableNode) {
        throw [InvalidOperationException]::new('SDK_CONFIG_FIELDS_MISSING')
    }

    $parsedYear = 0
    if (-not [int]::TryParse($yearNode.InnerText, [ref]$parsedYear) -or
        $parsedYear -notin @(2025, 2026)) {
        throw [InvalidOperationException]::new('SDK_CONFIG_YEAR_INVALID')
    }

    if ([string]::IsNullOrWhiteSpace($rootNode.InnerText) -or
        [string]::IsNullOrWhiteSpace($executableNode.InnerText)) {
        throw [InvalidOperationException]::new('SDK_CONFIG_PATHS_MISSING')
    }

    return [pscustomobject]@{
        PropsPath = $canonicalPropsPath
        Year = $parsedYear
        SdkRoot = Get-CadMaxCanonicalPath -Path $rootNode.InnerText
        ExecutablePath = Get-CadMaxCanonicalPath -Path $executableNode.InnerText
    }
}

function Assert-CadMaxAutoCADSdkBoundary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear,

        [Parameter(Mandatory)]
        [string]$PropsPath
    )

    $configuration = Read-CadMaxAutoCADLocalProps -PropsPath $PropsPath
    if ($configuration.Year -ne $AutoCADYear) {
        throw [InvalidOperationException]::new('SDK_CONFIG_YEAR_MISMATCH')
    }

    if (Test-CadMaxPathWithin -Candidate $configuration.SdkRoot -Root $script:CadMaxRepositoryRoot) {
        throw [InvalidOperationException]::new('SDK_ROOT_INSIDE_REPOSITORY')
    }

    if (-not (Test-Path -LiteralPath $configuration.SdkRoot -PathType Container)) {
        throw [InvalidOperationException]::new('SDK_ROOT_MISSING')
    }

    if (-not (Test-Path -LiteralPath $configuration.ExecutablePath -PathType Leaf) -or
        [IO.Path]::GetFileName($configuration.ExecutablePath) -ine 'acad.exe') {
        throw [InvalidOperationException]::new('AUTOCAD_EXECUTABLE_MISSING')
    }

    $dllVersions = Get-CadMaxManagedDllVersions `
        -AutoCADYear $AutoCADYear `
        -SdkRoot $configuration.SdkRoot

    $detectedInstallation = Get-CadMaxAutoCADInstallations |
        Where-Object {
            $_.Year -eq $AutoCADYear -and
            $_.SdkRoot -eq $configuration.SdkRoot -and
            $_.ExecutablePath -eq $configuration.ExecutablePath
        } |
        Select-Object -First 1
    if ($null -eq $detectedInstallation) {
        throw [InvalidOperationException]::new(
            'SDK_SOURCE_NOT_DETECTED_LOCAL_INSTALLATION')
    }

    return [pscustomobject]@{
        AutoCADYear = $AutoCADYear
        ProductVersion = $detectedInstallation.ProductVersion
        ManagedDllVersions = $dllVersions
        SdkRoot = $configuration.SdkRoot
        ExecutablePath = $configuration.ExecutablePath
        PropsPath = $configuration.PropsPath
        PathSummary = 'REDACTED'
    }
}

function Get-CadMaxDotNetExecutable {
    [CmdletBinding()]
    param()

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand -and (& $dotnetCommand.Source --list-sdks)) {
        return $dotnetCommand.Source
    }

    $localDotnet = Join-Path $script:CadMaxRepositoryRoot '.tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
        return $localDotnet
    }

    throw [InvalidOperationException]::new('DOTNET_8_SDK_MISSING')
}

function Get-CadMaxProductCode {
    [CmdletBinding()]
    param()

    return $script:CadMaxProductCode
}

function Get-CadMaxUpgradeCode {
    [CmdletBinding()]
    param()

    return $script:CadMaxUpgradeCode
}

function Get-CadMaxProjectVersion {
    [CmdletBinding()]
    param()

    $projectPath = Join-Path $script:CadMaxRepositoryRoot `
        'src\dotnet\CadMax.AutoCAD.Plugin\CadMax.AutoCAD.Plugin.csproj'
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode -or -not [Version]::TryParse($versionNode.InnerText, [ref]([Version]$null))) {
        throw [InvalidOperationException]::new('PROJECT_VERSION_INVALID')
    }

    return $versionNode.InnerText
}

function Get-CadMaxDevBundlePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear
    )

    return Join-Path $script:CadMaxBundleOutputRoot "CAD-MAX-$AutoCADYear.bundle"
}

function Assert-CadMaxBundleIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$BundlePath
    )

    Assert-CadMaxNoWildcard -Value $BundlePath
    $manifestPath = Join-Path $BundlePath 'PackageContents.xml'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw [InvalidOperationException]::new('BUNDLE_MANIFEST_MISSING')
    }

    try {
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    }
    catch {
        throw [InvalidOperationException]::new('BUNDLE_MANIFEST_INVALID_XML')
    }

    $applicationPackage = $manifest.DocumentElement
    # PowerShell's XML adapter exposes the manifest's Name attribute through
    # .Name, shadowing XmlNode.Name. LocalName always identifies the element.
    if ($null -eq $applicationPackage -or
        $applicationPackage.LocalName -ne 'ApplicationPackage') {
        throw [InvalidOperationException]::new('BUNDLE_MANIFEST_ROOT_INVALID')
    }

    if (-not $applicationPackage.GetAttribute('ProductCode').Equals(
            $script:CadMaxProductCode,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $applicationPackage.GetAttribute('UpgradeCode').Equals(
            $script:CadMaxUpgradeCode,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw [InvalidOperationException]::new('BUNDLE_IDENTITY_MISMATCH')
    }

    return $manifest
}

function Assert-CadMaxDevBundle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(2025, 2026)]
        [int]$AutoCADYear,

        [Parameter(Mandatory)]
        [string]$BundlePath,

        [string]$AllowedRoot = $script:CadMaxBundleOutputRoot
    )

    Assert-CadMaxNoWildcard -Value $BundlePath
    $canonicalBundlePath = Get-CadMaxCanonicalPath -Path $BundlePath
    $canonicalAllowedRoot = Get-CadMaxCanonicalPath -Path $AllowedRoot
    if (-not (Test-CadMaxPathWithin `
            -Candidate $canonicalBundlePath `
            -Root $canonicalAllowedRoot)) {
        throw [InvalidOperationException]::new('BUNDLE_LOCATION_UNSAFE')
    }

    if (-not (Test-Path -LiteralPath $canonicalBundlePath -PathType Container)) {
        throw [InvalidOperationException]::new('BUNDLE_MISSING')
    }

    $manifest = Assert-CadMaxBundleIdentity -BundlePath $canonicalBundlePath
    $applicationPackage = $manifest.DocumentElement
    $manifestPath = Join-Path $canonicalBundlePath 'PackageContents.xml'
    $manifestContent = Get-Content -LiteralPath $manifestPath -Raw
    if ($manifestContent -match '(?i)(?:[A-Z]:[\\/]|\\\\)') {
        throw [InvalidOperationException]::new('BUNDLE_MANIFEST_ABSOLUTE_PATH')
    }

    $projectVersion = Get-CadMaxProjectVersion
    if ($applicationPackage.GetAttribute('AppVersion') -ne $projectVersion -or
        $applicationPackage.GetAttribute('SchemaVersion') -ne '1.0') {
        throw [InvalidOperationException]::new('BUNDLE_VERSION_MISMATCH')
    }

    $runtimeRequirements = $manifest.SelectSingleNode(
        '/ApplicationPackage/Components/RuntimeRequirements')
    if ($null -eq $runtimeRequirements -or
        $runtimeRequirements.GetAttribute('OS') -ne 'Win64' -or
        $runtimeRequirements.GetAttribute('Platform') -ne 'AutoCAD' -or
        $runtimeRequirements.GetAttribute('SeriesMin') -ne (
            Get-CadMaxAutoCADSeries -AutoCADYear $AutoCADYear) -or
        $runtimeRequirements.GetAttribute('SeriesMax') -ne (
            Get-CadMaxAutoCADSeries -AutoCADYear $AutoCADYear)) {
        throw [InvalidOperationException]::new('BUNDLE_RUNTIME_REQUIREMENTS_INVALID')
    }

    $componentEntries = @($manifest.SelectNodes(
            '/ApplicationPackage/Components/ComponentEntry'))
    if ($componentEntries.Count -ne 1) {
        throw [InvalidOperationException]::new('BUNDLE_COMPONENT_INVALID')
    }

    $componentEntry = $componentEntries[0]
    if (
        $componentEntry.GetAttribute('LoadReasons') -ne 'LoadOnAutoCADStartup') {
        throw [InvalidOperationException]::new('BUNDLE_COMPONENT_INVALID')
    }

    $moduleName = $componentEntry.GetAttribute('ModuleName')
    $moduleSegments = @($moduleName -split '[\\/]')
    if ([string]::IsNullOrWhiteSpace($moduleName) -or
        [IO.Path]::IsPathRooted($moduleName) -or
        $moduleName.Contains(':') -or
        $moduleSegments -contains '..') {
        throw [InvalidOperationException]::new('BUNDLE_MODULE_PATH_UNSAFE')
    }

    $relativeModule = $moduleName.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ($relativeModule.StartsWith(".$([IO.Path]::DirectorySeparatorChar)")) {
        $relativeModule = $relativeModule.Substring(2)
    }
    $modulePath = Get-CadMaxCanonicalPath -Path (
        Join-Path $canonicalBundlePath $relativeModule)
    if (-not (Test-CadMaxPathWithin -Candidate $modulePath -Root $canonicalBundlePath) -or
        -not (Test-Path -LiteralPath $modulePath -PathType Leaf) -or
        [IO.Path]::GetFileName($modulePath) -ine 'CadMax.AutoCAD.Adapter.dll') {
        throw [InvalidOperationException]::new('BUNDLE_ADAPTER_MISSING')
    }

    $statusCommands = @($componentEntry.SelectNodes('./Commands/Command'))
    if ($statusCommands.Count -ne 1) {
        throw [InvalidOperationException]::new('BUNDLE_STATUS_COMMAND_INVALID')
    }

    $statusCommand = $statusCommands[0]
    if (
        $statusCommand.GetAttribute('Global') -ne 'CADMAXPLUGINSTATUS' -or
        $statusCommand.GetAttribute('Local') -ne 'CADMAXPLUGINSTATUS') {
        throw [InvalidOperationException]::new('BUNDLE_STATUS_COMMAND_INVALID')
    }

    $allowedBundleFiles = @(
        'PackageContents.xml',
        'Contents/Windows/CadMax.AutoCAD.Adapter.deps.json',
        'Contents/Windows/CadMax.AutoCAD.Adapter.dll',
        'Contents/Windows/CadMax.AutoCAD.Plugin.dll',
        'Contents/Windows/CadMax.Bridge.Core.dll',
        'Contents/Windows/CadMax.Contracts.dll'
    )
    $bundleFiles = @(Get-ChildItem -LiteralPath $canonicalBundlePath -File -Recurse)
    foreach ($bundleFile in $bundleFiles) {
        $relativeFile = $bundleFile.FullName.
            Substring($canonicalBundlePath.Length).
            TrimStart([char[]]@('\', '/')).
            Replace('\', '/')
        if ($relativeFile -notin $allowedBundleFiles) {
            if ($bundleFile.Name -iin @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll')) {
                throw [InvalidOperationException]::new('AUTODESK_BINARY_DETECTED')
            }

            throw [InvalidOperationException]::new('BUNDLE_FILE_NOT_ALLOWED')
        }
    }

    foreach ($requiredRelativeFile in $allowedBundleFiles) {
        $requiredFilePath = Join-Path `
            $canonicalBundlePath `
            $requiredRelativeFile.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $requiredFilePath -PathType Leaf)) {
            throw [InvalidOperationException]::new('BUNDLE_REQUIRED_FILE_MISSING')
        }
    }

    return [pscustomobject]@{
        AutoCADYear = $AutoCADYear
        AppVersion = $projectVersion
        Series = Get-CadMaxAutoCADSeries -AutoCADYear $AutoCADYear
        ModuleName = $moduleName
        AutodeskBinaryCount = 0
        BundlePath = $canonicalBundlePath
    }
}
