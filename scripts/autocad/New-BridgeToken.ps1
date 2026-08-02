[CmdletBinding()]
param(
    [switch]$Rotate,

    [string]$TokenFilePath
)

$ErrorActionPreference = 'Stop'
$temporaryFile = $null
$backupFile = $null
$tokenBytes = $null

function Set-CadMaxBridgeTokenAcl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUser) {
        throw [InvalidOperationException]::new('CURRENT_USER_SID_UNAVAILABLE')
    }
    $system = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null)
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $currentUser,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $system,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
    [IO.FileSystemAclExtensions]::SetAccessControl(
        [IO.FileInfo]::new($Path),
        $security)
}

function New-CadMaxBridgeTokenFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Payload
    )

    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUser) {
        throw [InvalidOperationException]::new('CURRENT_USER_SID_UNAVAILABLE')
    }
    $system = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null)
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetOwner($currentUser)
    $security.SetAccessRuleProtection($true, $false)
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $currentUser,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $system,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))

    $payloadBytes = [Text.UTF8Encoding]::new($false).GetBytes($Payload)
    $stream = $null
    try {
        $stream = [IO.FileSystemAclExtensions]::Create(
            [IO.FileInfo]::new($Path),
            [IO.FileMode]::CreateNew,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [IO.FileShare]::None,
            4096,
            [IO.FileOptions]::WriteThrough,
            $security)
        $stream.Write($payloadBytes, 0, $payloadBytes.Length)
        $stream.Flush($true)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        [Array]::Clear($payloadBytes, 0, $payloadBytes.Length)
    }
}

function Assert-CadMaxBridgeTokenAcl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUser) {
        throw [InvalidOperationException]::new('CURRENT_USER_SID_UNAVAILABLE')
    }
    $system = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null)
    $security = [IO.FileSystemAclExtensions]::GetAccessControl(
        [IO.FileInfo]::new($Path),
        [Security.AccessControl.AccessControlSections]::Access -bor
        [Security.AccessControl.AccessControlSections]::Owner)
    if (-not $security.AreAccessRulesProtected) {
        throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
    }
    $owner = $security.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner.Equals($currentUser)) {
        throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
    }

    $currentRead = $false
    $currentWrite = $false
    $systemRead = $false
    $rules = $security.GetAccessRules(
        $true,
        $true,
        [Security.Principal.SecurityIdentifier])
    foreach ($rule in $rules) {
        if ($rule.IsInherited -or
            $rule.IdentityReference -isnot [Security.Principal.SecurityIdentifier] -or
            $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) {
            throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
        }

        $identity = [Security.Principal.SecurityIdentifier]$rule.IdentityReference
        $canRead = ($rule.FileSystemRights -band (
                [Security.AccessControl.FileSystemRights]::ReadData -bor
                [Security.AccessControl.FileSystemRights]::Read)) -ne 0
        $canWrite = ($rule.FileSystemRights -band (
                [Security.AccessControl.FileSystemRights]::WriteData -bor
                [Security.AccessControl.FileSystemRights]::Write)) -ne 0
        if ($identity.Equals($currentUser)) {
            $currentRead = $currentRead -or $canRead
            $currentWrite = $currentWrite -or $canWrite
        }
        elseif ($identity.Equals($system)) {
            $systemRead = $systemRead -or $canRead
        }
        elseif ($rule.FileSystemRights -ne 0) {
            throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
        }
    }
    if (-not ($currentRead -and $currentWrite -and $systemRead)) {
        throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
    }
}

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $repositoryRoot = Get-CadMaxRepositoryRoot
    $defaultTokenFile = Join-Path (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'CAD-MAX\config\bridge-token.json'
    $testRoot = Join-Path $repositoryRoot '.tools\autocad\bridge-token-tests'
    $targetFile = if ([string]::IsNullOrWhiteSpace($TokenFilePath)) {
        Get-CadMaxCanonicalPath -Path $defaultTokenFile
    }
    else {
        Assert-CadMaxNoWildcard -Value $TokenFilePath
        $candidate = Get-CadMaxCanonicalPath -Path $TokenFilePath
        if (-not (Test-CadMaxPathWithin -Candidate $candidate -Root $testRoot)) {
            throw [InvalidOperationException]::new('TOKEN_TARGET_UNSAFE')
        }
        $candidate
    }

    $targetDirectory = Split-Path -Parent $targetFile
    [IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
    $alreadyExists = Test-Path -LiteralPath $targetFile -PathType Leaf
    if ($alreadyExists -and -not $Rotate) {
        throw [InvalidOperationException]::new('TOKEN_ALREADY_CONFIGURED')
    }
    if ($alreadyExists) {
        # File.Replace can preserve the destination DACL. Rotation therefore admits only a
        # fully secure target before replacing it, not merely a correctly owned file.
        Assert-CadMaxBridgeTokenAcl -Path $targetFile
    }

    $tokenBytes = [byte[]]::new(32)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($tokenBytes)
    }
    finally {
        $generator.Dispose()
    }
    $encodedToken = [Convert]::ToBase64String($tokenBytes).
        TrimEnd('=').
        Replace('+', '-').
        Replace('/', '_')
    if ($encodedToken.Length -ne 43 -or $encodedToken -notmatch '^[A-Za-z0-9_-]{43}$') {
        throw [InvalidOperationException]::new('TOKEN_GENERATION_FAILED')
    }

    $payload = [ordered]@{
        schemaVersion = '1.0'
        token = $encodedToken
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString(
            'o',
            [Globalization.CultureInfo]::InvariantCulture)
    } | ConvertTo-Json -Compress
    $temporaryFile = Join-Path $targetDirectory (
        ".bridge-token-$([Guid]::NewGuid().ToString('N')).tmp")
    New-CadMaxBridgeTokenFile -Path $temporaryFile -Payload $payload

    if ($alreadyExists) {
        $backupFile = Join-Path $targetDirectory (
            ".bridge-token-$([Guid]::NewGuid().ToString('N')).bak")
        [IO.File]::Replace($temporaryFile, $targetFile, $backupFile, $true)
    }
    else {
        [IO.File]::Move($temporaryFile, $targetFile)
    }
    $temporaryFile = $null
    Set-CadMaxBridgeTokenAcl -Path $targetFile
    Assert-CadMaxBridgeTokenAcl -Path $targetFile
    if ($null -ne $backupFile) {
        Remove-Item -LiteralPath $backupFile -Force
        $backupFile = $null
    }

    $digest = [Security.Cryptography.SHA256]::HashData($tokenBytes)
    try {
        $tokenId = ([Convert]::ToHexString($digest)).Substring(0, 12).ToLowerInvariant()
    }
    finally {
        [Array]::Clear($digest, 0, $digest.Length)
    }

    [pscustomobject]@{
        tokenFileState = if ($alreadyExists) { 'ROTATED' } else { 'CREATED' }
        aclState = 'SECURE'
        tokenId = $tokenId
        path = 'REDACTED'
    } | ConvertTo-Json -Compress
    exit 0
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
    if ($null -ne $tokenBytes) {
        [Array]::Clear($tokenBytes, 0, $tokenBytes.Length)
    }
    if ($null -ne $temporaryFile -and (Test-Path -LiteralPath $temporaryFile -PathType Leaf)) {
        Remove-Item -LiteralPath $temporaryFile -Force
    }
    if ($null -ne $backupFile -and (Test-Path -LiteralPath $backupFile -PathType Leaf)) {
        Remove-Item -LiteralPath $backupFile -Force
    }
}
