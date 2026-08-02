<#
.SYNOPSIS
在用户通过 AutoCAD UI 准备的可丢弃 fixture 上记录 Phase 1.4 脱敏只读 evidence。
.DESCRIPTION
脚本不启动命令、不使用外部自动化接口，也不创建或修改图元。执行顺序固定为：
UI 读取 DBMOD-before → drawing-doctor reads → UI 读取 DBMOD-after → UI close without save → final SHA-256。
fixture 只以安全 basename 传入 doctor；doctor 在不输出名称的前提下验证该 basename、active document ID 和
document list active marker 一致。完整路径从不传给 bridge 或写入输出。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(2025, 2026)]
    [int]$AutoCADYear,

    [Parameter(Mandatory)]
    [string]$FixturePath,

    [switch]$ConfirmDisposableFixture,

    [switch]$ConfirmActiveFixture
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$uv = Join-Path $repositoryRoot '.tools\uv\bin\uv.exe'

function Read-CadMaxDbmod {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Step
    )

    [int]$dbmod = 0
    $enteredValue = Read-Host $Step
    if (-not [int]::TryParse(
            $enteredValue,
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$dbmod) -or $dbmod -lt 0) {
        throw [InvalidOperationException]::new('DBMOD_INPUT_INVALID')
    }
    return $dbmod
}

try {
    if (-not $ConfirmDisposableFixture) {
        throw [InvalidOperationException]::new('DISPOSABLE_FIXTURE_CONFIRMATION_REQUIRED')
    }
    if (-not $ConfirmActiveFixture) {
        throw [InvalidOperationException]::new('ACTIVE_FIXTURE_CONFIRMATION_REQUIRED')
    }
    if (-not (Test-Path -LiteralPath $FixturePath -PathType Leaf)) {
        throw [InvalidOperationException]::new('FIXTURE_NOT_FOUND')
    }
    if (-not (Test-Path -LiteralPath $uv -PathType Leaf)) {
        throw [InvalidOperationException]::new('UV_EXECUTABLE_MISSING')
    }

    $tokenCheck = @(& (Join-Path $PSScriptRoot 'Test-BridgeToken.ps1') -MachineLocal 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('MACHINE_TOKEN_VALIDATION_FAILED')
    }
    $tokenFile = Join-Path (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'CAD-MAX\config\bridge-token.json'
    $tokenPayload = Get-Content -LiteralPath $tokenFile -Raw | ConvertFrom-Json
    $token = [string]$tokenPayload.token
    if ([string]::IsNullOrWhiteSpace($token) -or ($tokenCheck -join "`n").Contains($token)) {
        throw [InvalidOperationException]::new('TOKEN_OUTPUT_DISCLOSURE')
    }

    $fixtureName = [IO.Path]::GetFileName($FixturePath)
    if ([string]::IsNullOrWhiteSpace($fixtureName) -or
        $fixtureName.Length -gt 128 -or
        $fixtureName -match '[\\/:\x00-\x1F\x7F]') {
        throw [InvalidOperationException]::new('FIXTURE_BASENAME_UNSAFE')
    }

    $hashBefore = (Get-FileHash -LiteralPath $FixturePath -Algorithm SHA256).Hash
    Write-Host (
        "AutoCAD $($AutoCADYear): UI step 1 of 4. Open the confirmed disposable fixture, make it " +
        'the active document, verify its UI-visible basename, then enter that active document''s DBMOD-before value.')
    $dbmodBefore = Read-CadMaxDbmod -Step 'DBMOD-before (non-negative integer)'

    $doctorInfo = [Diagnostics.ProcessStartInfo]::new()
    $doctorInfo.FileName = $uv
    [void]$doctorInfo.ArgumentList.Add('run')
    [void]$doctorInfo.ArgumentList.Add('cad-max-mcp')
    [void]$doctorInfo.ArgumentList.Add('drawing-doctor')
    [void]$doctorInfo.ArgumentList.Add('--expected-active-document-name')
    [void]$doctorInfo.ArgumentList.Add($fixtureName)
    $doctorInfo.WorkingDirectory = $repositoryRoot
    $doctorInfo.UseShellExecute = $false
    $doctorInfo.CreateNoWindow = $true
    $doctorInfo.RedirectStandardOutput = $true
    $doctorInfo.RedirectStandardError = $true
    $doctorInfo.Environment['CAD_MAX_BRIDGE_URL'] = 'http://127.0.0.1:47770'
    $doctorInfo.Environment['CAD_MAX_BRIDGE_TOKEN_FILE'] = $tokenFile
    $doctorProcess = [Diagnostics.Process]::Start($doctorInfo)
    if ($null -eq $doctorProcess) {
        throw [InvalidOperationException]::new('DRAWING_DOCTOR_START_FAILED')
    }
    try {
        $doctorOutputTask = $doctorProcess.StandardOutput.ReadToEndAsync()
        $doctorErrorTask = $doctorProcess.StandardError.ReadToEndAsync()
        if (-not $doctorProcess.WaitForExit(60000)) {
            $doctorProcess.Kill($true)
            throw [InvalidOperationException]::new('DRAWING_DOCTOR_TIMEOUT')
        }
        $doctorOutput = $doctorOutputTask.GetAwaiter().GetResult()
        $doctorError = $doctorErrorTask.GetAwaiter().GetResult()
        if ($doctorOutput.Contains($token) -or $doctorError.Contains($token)) {
            throw [InvalidOperationException]::new('TOKEN_OUTPUT_DISCLOSURE')
        }
        if ($doctorProcess.ExitCode -ne 0) {
            $doctorFailureCode = 'DRAWING_DOCTOR_FAILED'
            try {
                $doctorFailure = $doctorOutput | ConvertFrom-Json
                $doctorStatus = [string]$doctorFailure.status
                if ($doctorStatus -match '^[A-Z][A-Z0-9_]{0,95}$') {
                    $doctorFailureCode = "DRAWING_DOCTOR_$doctorStatus"
                }
            }
            catch {
                # Keep the fixed safe fallback; raw stderr is never relayed.
            }
            throw [InvalidOperationException]::new($doctorFailureCode)
        }
    }
    finally {
        $doctorProcess.Dispose()
    }
    $doctorText = $doctorOutput
    $doctor = $doctorText | ConvertFrom-Json
    $requiredChecks = @(
        'sameInstanceId',
        'sameActiveDocumentId',
        'mainThreadVerified',
        'applicationContext',
        'documentCommandContext',
        'readOnlyEvidence',
        'transactionTruthful',
        'documentCountConsistent',
        'activeDocumentMatchesExpectedName',
        'capabilityHonesty',
        'writeScriptObjectCapabilitiesFalse',
        'healthDwgRead'
    )
    foreach ($checkName in $requiredChecks) {
        $check = $doctor.checks.PSObject.Properties[$checkName]
        if ($null -eq $check -or $check.Value -ne $true) {
            throw [InvalidOperationException]::new('DRAWING_DOCTOR_EVIDENCE_INVALID')
        }
    }
    if ($doctor.status -ne 'OK' -or
        $doctor.activeDocument -ne 'REDACTED' -or
        -not $doctor.dwgRead -or
        $doctor.dwgWrite -or
        $doctor.allowWrite -or
        $doctor.allowScript -or
        $doctorText.Contains($FixturePath, [StringComparison]::Ordinal) -or
        $doctorText -match '(?i)(?:[A-Z]:[\\/]|\\\\)') {
        throw [InvalidOperationException]::new('DRAWING_DOCTOR_EVIDENCE_INVALID')
    }

    Write-Host (
        'UI step 2 of 4. Without changing the same active fixture, inspect DBMOD after the read ' +
        'operations and enter the value.')
    $dbmodAfter = Read-CadMaxDbmod -Step 'DBMOD-after (non-negative integer)'
    if ($dbmodBefore -ne $dbmodAfter) {
        throw [InvalidOperationException]::new('DBMOD_CHANGED')
    }

    Write-Host (
        'UI step 3 of 4. Close the same active fixture without saving. After the document is closed ' +
        'and no save has occurred, press Enter for the final digest check.')
    [void](Read-Host)

    $hashAfter = (Get-FileHash -LiteralPath $FixturePath -Algorithm SHA256).Hash
    if ($hashBefore -ne $hashAfter) {
        throw [InvalidOperationException]::new('DWG_HASH_CHANGED')
    }

    [pscustomobject]@{
        result = 'PASS'
        autoCADYear = $AutoCADYear
        activeDocument = 'REDACTED'
        fixtureBinding = 'ACTIVE_BASENAME_MATCHED'
        documentCount = $doctor.documentCount
        layoutCount = $doctor.layoutCount
        boundsState = $doctor.boundsState
        dbmod = 'UNCHANGED'
        readInvariantOrder = 'DBMOD_BEFORE_READS_DBMOD_AFTER_CLOSE_WITHOUT_SAVE_HASH'
        dwgSha256 = 'UNCHANGED'
        pathSentinel = 'ABSENT'
        readOnly = $true
        allowWrite = $false
        allowScript = $false
        paths = 'REDACTED'
    } | ConvertTo-Json -Compress
    exit 0
}
catch {
    $exceptionMessage = [string]$_.Exception.Message
    $safeErrorCode = if ($exceptionMessage -match '^[A-Z][A-Z0-9_]{0,127}$') {
        $exceptionMessage
    }
    else {
        'INTERNAL_ERROR'
    }
    Write-Output "ERROR / $safeErrorCode"
    exit 1
}
