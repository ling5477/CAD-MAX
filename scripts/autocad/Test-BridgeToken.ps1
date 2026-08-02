[CmdletBinding()]
param(
    [switch]$MachineLocal
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = $null

function Test-CadMaxBridgeTokenAcl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null)
    if ($null -eq $currentUser) {
        return $false
    }
    $security = [IO.FileSystemAclExtensions]::GetAccessControl(
        [IO.FileInfo]::new($Path),
        [Security.AccessControl.AccessControlSections]::Access -bor
        [Security.AccessControl.AccessControlSections]::Owner)
    if (-not $security.AreAccessRulesProtected) {
        return $false
    }
    $owner = $security.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner.Equals($currentUser)) {
        return $false
    }

    $currentRead = $false
    $currentWrite = $false
    $systemRead = $false
    $rules = $security.GetAccessRules(
        $true,
        $true,
        [Security.Principal.SecurityIdentifier])
    foreach ($rule in $rules) {
        if ($rule.IsInherited) {
            return $false
        }
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) {
            continue
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
            return $false
        }
    }
    return $currentRead -and $currentWrite -and $systemRead
}

function Assert-CadMaxBridgeTokenAcl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-CadMaxBridgeTokenAcl -Path $Path)) {
        throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
    }
}

function Set-CadMaxBridgeTokenFixtureAcl {
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
    [IO.FileSystemAclExtensions]::SetAccessControl(
        [IO.FileInfo]::new($Path),
        $security)
}

function Read-CadMaxBridgeTokenSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw [InvalidOperationException]::new('TOKEN_NOT_CONFIGURED')
    }
    if (-not (Test-CadMaxBridgeTokenAcl -Path $Path)) {
        throw [InvalidOperationException]::new('TOKEN_FILE_INSECURE')
    }
    try {
        $payload = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw [InvalidOperationException]::new('TOKEN_CONFIG_INVALID')
    }
    $propertyNames = @($payload.PSObject.Properties.Name)
    $sortedPropertyNames = (@($propertyNames | Sort-Object) -join ',')
    $expectedPropertyNames = (@('createdAtUtc', 'schemaVersion', 'token') -join ',')
    if ($sortedPropertyNames -ne $expectedPropertyNames -or
        $payload.schemaVersion -ne '1.0' -or
        [string]$payload.token -notmatch '^[A-Za-z0-9_-]{43}$' -or
        [string]::IsNullOrWhiteSpace([string]$payload.createdAtUtc)) {
        throw [InvalidOperationException]::new('TOKEN_CONFIG_INVALID')
    }
    $base64 = ([string]$payload.token).Replace('-', '+').Replace('_', '/') + '='
    try {
        $bytes = [Convert]::FromBase64String($base64)
    }
    catch {
        throw [InvalidOperationException]::new('TOKEN_INVALID')
    }
    try {
        if ($bytes.Length -ne 32) {
            throw [InvalidOperationException]::new('TOKEN_INVALID')
        }
        $digest = [Security.Cryptography.SHA256]::HashData($bytes)
        try {
            return [pscustomobject]@{
                Token = [string]$payload.token
                TokenId = ([Convert]::ToHexString($digest)).Substring(0, 12).ToLowerInvariant()
            }
        }
        finally {
            [Array]::Clear($digest, 0, $digest.Length)
        }
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $repositoryRoot = Get-CadMaxRepositoryRoot
    if ($MachineLocal) {
        $tokenFile = Join-Path (
            [Environment]::GetFolderPath(
                [Environment+SpecialFolder]::LocalApplicationData)) `
            'CAD-MAX\config\bridge-token.json'
        $summary = Read-CadMaxBridgeTokenSummary -Path $tokenFile
        [pscustomobject]@{
            result = 'PASS'
            tokenFileState = 'CONFIGURED'
            aclState = 'SECURE'
            tokenId = $summary.TokenId
            path = 'REDACTED'
        } | ConvertTo-Json -Compress
        exit 0
    }

    $testRoot = Join-Path $repositoryRoot '.tools\autocad\bridge-token-tests'
    $fixtureRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $fixtureFile = Join-Path $fixtureRoot 'bridge-token.json'
    $powerShell = Get-CadMaxPowerShellExecutable
    $newTokenScript = Join-Path $PSScriptRoot 'New-BridgeToken.ps1'

    $createOutput = @(& $powerShell -NoLogo -NoProfile -ExecutionPolicy Bypass `
            -File $newTokenScript -TokenFilePath $fixtureFile 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('TOKEN_CREATE_TEST_FAILED')
    }
    $created = Read-CadMaxBridgeTokenSummary -Path $fixtureFile
    Assert-CadMaxBridgeTokenAcl -Path $fixtureFile
    $createText = $createOutput -join "`n"
    if ($createText.Contains($created.Token) -or
        $createText -match '(?i)(?:[A-Z]:[\\/]|\\\\)') {
        throw [InvalidOperationException]::new('TOKEN_OUTPUT_DISCLOSURE')
    }

    $duplicateOutput = @(& $powerShell -NoLogo -NoProfile -ExecutionPolicy Bypass `
            -File $newTokenScript -TokenFilePath $fixtureFile 2>&1)
    if ($LASTEXITCODE -eq 0 -or
        ($duplicateOutput -join "`n") -notmatch 'TOKEN_ALREADY_CONFIGURED') {
        throw [InvalidOperationException]::new('TOKEN_OVERWRITE_GUARD_FAILED')
    }

    $security = [IO.FileSystemAclExtensions]::GetAccessControl(
        [IO.FileInfo]::new($fixtureFile))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new(
                [Security.Principal.WellKnownSidType]::BuiltinUsersSid,
                $null),
            [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
            [Security.AccessControl.FileSystemRights]::TakeOwnership,
            [Security.AccessControl.AccessControlType]::Allow))
    [IO.FileSystemAclExtensions]::SetAccessControl(
        [IO.FileInfo]::new($fixtureFile),
        $security)
    $insecureRotateOutput = @(& $powerShell -NoLogo -NoProfile -ExecutionPolicy Bypass `
            -File $newTokenScript -TokenFilePath $fixtureFile -Rotate 2>&1)
    if ($LASTEXITCODE -eq 0 -or
        ($insecureRotateOutput -join "`n") -notmatch 'TOKEN_FILE_INSECURE') {
        throw [InvalidOperationException]::new('TOKEN_INSECURE_ROTATION_NOT_REJECTED')
    }
    Set-CadMaxBridgeTokenFixtureAcl -Path $fixtureFile
    $afterRejectedRotate = Read-CadMaxBridgeTokenSummary -Path $fixtureFile
    if ($afterRejectedRotate.Token -ne $created.Token) {
        throw [InvalidOperationException]::new('TOKEN_INSECURE_ROTATION_MUTATED')
    }

    $rotateOutput = @(& $powerShell -NoLogo -NoProfile -ExecutionPolicy Bypass `
            -File $newTokenScript -TokenFilePath $fixtureFile -Rotate 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('TOKEN_ROTATE_TEST_FAILED')
    }
    $rotated = Read-CadMaxBridgeTokenSummary -Path $fixtureFile
    Assert-CadMaxBridgeTokenAcl -Path $fixtureFile
    $rotateText = $rotateOutput -join "`n"
    if ($created.Token -eq $rotated.Token -or
        $created.TokenId -eq $rotated.TokenId -or
        $rotateText.Contains($created.Token) -or
        $rotateText.Contains($rotated.Token)) {
        throw [InvalidOperationException]::new('TOKEN_ROTATE_OR_REDACTION_FAILED')
    }

    $security = [IO.FileSystemAclExtensions]::GetAccessControl(
        [IO.FileInfo]::new($fixtureFile))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new(
                [Security.Principal.WellKnownSidType]::BuiltinUsersSid,
                $null),
            [Security.AccessControl.FileSystemRights]::Read,
            [Security.AccessControl.AccessControlType]::Allow))
    [IO.FileSystemAclExtensions]::SetAccessControl(
        [IO.FileInfo]::new($fixtureFile),
        $security)
    if (Test-CadMaxBridgeTokenAcl -Path $fixtureFile) {
        throw [InvalidOperationException]::new('TOKEN_BROAD_ACL_NOT_REJECTED')
    }

    [pscustomobject]@{
        result = 'PASS'
        cases = 12
        aclState = 'SECURE_AND_BROAD_NEGATIVE_VERIFIED'
        token = 'REDACTED'
        paths = 'REDACTED'
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
    if ($null -ne $fixtureRoot -and (Test-Path -LiteralPath $fixtureRoot)) {
        if (Test-CadMaxPathWithin `
                -Candidate $fixtureRoot `
                -Root (Join-Path (Get-CadMaxRepositoryRoot) '.tools\autocad\bridge-token-tests')) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}
