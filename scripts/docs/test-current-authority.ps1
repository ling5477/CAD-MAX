<#
.SYNOPSIS
回归 contract 驱动的 schema 1/schema 2 authority、transition、evidence 与 ROADMAP hard gate。
.NOTES
fixture 仅写入 Git 忽略的 .tools/governance-tests/，每次运行后删除本次目录。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$checkerPath = Join-Path $PSScriptRoot 'check-current-authority.ps1'
$powerShellExecutable = (Get-Process -Id $PID).Path
$fixtureBase = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot '.tools\governance-tests'))
$runRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $fixtureBase ([guid]::NewGuid().ToString('N'))))
$fixturePrefix = $fixtureBase.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $runRoot.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe fixture path: $runRoot"
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Content
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $Content,
        (New-Object System.Text.UTF8Encoding($false)))
}

function New-Schema2Status {
    param(
        [string] $AcceptedBatch = 'PHASE_1_PLAN',
        [string] $AcceptedCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
        [string] $AcceptedRun = '123',
        [string] $WorkBatch = 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP',
        [string] $WorkStatus = 'NOT_STARTED',
        [string] $WorkCommit = 'NONE',
        [string] $WorkRun = 'NOT_RUN',
        [string] $NextAction = 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP',
        [string] $AutoCadRuntime = 'NOT_CONNECTED',
        [string] $DwgRead = 'NOT_IMPLEMENTED',
        [string] $DwgWrite = 'NOT_IMPLEMENTED',
        [string] $ReadOnly = 'ENABLED',
        [string] $AllowWrite = 'DISABLED',
        [string] $AllowScript = 'DISABLED',
        [string] $HttpBinding = 'LOOPBACK_ONLY'
    )

    $workBodyStatus = $WorkStatus.Replace('|', ' / ')
    return @"
# 当前状态

<!-- cad-max-current-authority:start
authority_schema=2
current_phase=PHASE_1
phase_status=IN_PROGRESS|NOT_FROZEN
accepted_work_batch=$AcceptedBatch
accepted_work_batch_status=ACCEPTED|CI_GREEN
accepted_work_batch_commit=$AcceptedCommit
accepted_work_batch_ci_run=$AcceptedRun
work_batch=$WorkBatch
work_batch_status=$WorkStatus
work_batch_commit=$WorkCommit
work_batch_ci_run=$WorkRun
next_action=$NextAction
autocad_runtime=$AutoCadRuntime
dwg_read=$DwgRead
dwg_write=$DwgWrite
read_only=$ReadOnly
allow_write=$AllowWrite
allow_script=$AllowScript
http_binding=$HttpBinding
cad-max-current-authority:end -->

- Phase 1：`IN PROGRESS / NOT FROZEN`（进行中 / 未冻结）。
- $AcceptedBatch：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）。
- $WorkBatch：$workBodyStatus。
- 下一动作：`$NextAction`。
"@
}

function New-Fixture {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][AllowEmptyString()][string] $StatusContent,
        [Parameter(Mandatory)][string] $RoadmapAction,
        [switch] $InvalidEvidenceName
    )

    $caseRoot = Join-Path $runRoot $Name
    $evidenceRoot = Join-Path $caseRoot 'evidence'
    New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
    $statusPath = Join-Path $caseRoot 'STATUS.md'
    $roadmapPath = Join-Path $caseRoot 'ROADMAP.md'
    Write-Utf8File $statusPath $StatusContent
    Write-Utf8File $roadmapPath "# 当前路线`n`n唯一下一动作：``$RoadmapAction``。`n"
    Write-Utf8File (Join-Path $evidenceRoot 'README.md') (
        "# Fixture evidence`n`n此索引仅用于 authority checker regression，内容完整且不决定 current authority。`n")
    $attemptName = if ($InvalidEvidenceName) {
        'INVALID.attempt-1.md'
    }
    else {
        'FIXTURE.attempt-01.md'
    }
    Write-Utf8File (Join-Path $evidenceRoot $attemptName) @"
# Fixture attempt

本文件仅用于本地 regression。它包含足够正文以验证 evidence 非空约束、两位 attempt 命名与 fail-closed 行为；不描述任何真实 AutoCAD、DWG、GitHub Actions 或生产事实。

- Validation：fixture only。
- Safety：read-only；write/script disabled。
"@

    return [pscustomobject]@{
        Status = $statusPath
        Roadmap = $roadmapPath
        Evidence = $evidenceRoot
    }
}

function Invoke-AuthorityChecker {
    param(
        [Parameter(Mandatory)] $Fixture,
        [string] $PreviousStatusPath = ''
    )

    $arguments = @(
        '-NoLogo', '-NoProfile', '-File', $checkerPath,
        '-StatusPath', $Fixture.Status,
        '-RoadmapPath', $Fixture.Roadmap,
        '-EvidenceRoot', $Fixture.Evidence
    )
    if (-not [string]::IsNullOrWhiteSpace($PreviousStatusPath)) {
        $arguments += @('-PreviousStatusPath', $PreviousStatusPath)
    }
    $output = @(& $powerShellExecutable @arguments 2>&1)
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text = ($output -join [Environment]::NewLine)
    }
}

function Assert-Positive {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)] $Fixture
    )

    $result = Invoke-AuthorityChecker $Fixture
    if ($result.ExitCode -ne 0 -or
        $result.Text -notmatch 'PASS / CURRENT_AUTHORITY_CONSISTENT') {
        throw "Positive authority regression failed: $Name`n$($result.Text)"
    }
}

function Assert-Negative {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $ExpectedPattern,
        [string] $PreviousStatusPath = ''
    )

    $result = Invoke-AuthorityChecker $Fixture $PreviousStatusPath
    if ($result.ExitCode -eq 0 -or $result.Text -notmatch $ExpectedPattern) {
        throw "Negative authority regression failed: $Name expected=$ExpectedPattern`n$($result.Text)"
    }
}

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
try {
    $currentFixture = [pscustomobject]@{
        Status = Join-Path $repositoryRoot 'docs\current\STATUS.md'
        Roadmap = Join-Path $repositoryRoot 'docs\current\ROADMAP.md'
        Evidence = Join-Path $repositoryRoot 'docs\current\evidence\phase-1'
    }
    Assert-Positive 'current repository authority' $currentFixture

    $validSchema2 = New-Schema2Status
    $schema2Fixture = New-Fixture `
        -Name 'schema2-positive' `
        -StatusContent $validSchema2 `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Positive 'schema 2 initial work batch' $schema2Fixture

    $maintenanceReconciliationStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP' `
        -WorkBatch 'PHASE_1_MAINTENANCE_DEPENDENCIES' `
        -NextAction 'IMPLEMENT_PHASE_1_MAINTENANCE_DEPENDENCIES'
    $maintenanceReconciliation = New-Fixture `
        -Name 'maintenance-reconciliation-positive' `
        -StatusContent $maintenanceReconciliationStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_MAINTENANCE_DEPENDENCIES'
    Assert-Positive 'maintenance reconciliation' $maintenanceReconciliation

    $maintenancePendingStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP' `
        -WorkBatch 'PHASE_1_MAINTENANCE_DEPENDENCIES' `
        -WorkStatus 'COMMITTED|CI_PENDING' `
        -WorkCommit 'PENDING' `
        -WorkRun 'PENDING' `
        -NextAction 'PHASE_1_MAINTENANCE_DEPENDENCIES_CI_WAIT_OR_INVESTIGATION'
    $maintenancePending = New-Fixture `
        -Name 'maintenance-pending-positive' `
        -StatusContent $maintenancePendingStatus `
        -RoadmapAction 'PHASE_1_MAINTENANCE_DEPENDENCIES_CI_WAIT_OR_INVESTIGATION'
    Assert-Positive 'maintenance pending without future SHA' $maintenancePending

    $maintenanceReturnStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_MAINTENANCE_DEPENDENCIES' `
        -AcceptedCommit 'dddddddddddddddddddddddddddddddddddddddd' `
        -AcceptedRun '456' `
        -WorkBatch 'PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE' `
        -NextAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    $maintenanceReturn = New-Fixture `
        -Name 'maintenance-return-positive' `
        -StatusContent $maintenanceReturnStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    Assert-Positive 'return to Phase 1.2 after maintenance' $maintenanceReturn

    $phase12AcceptedStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE' `
        -AcceptedCommit 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' `
        -AcceptedRun '789' `
        -WorkBatch 'PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH' `
        -NextAction 'IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH' `
        -AutoCadRuntime 'CONNECTED'
    $phase12Accepted = New-Fixture `
        -Name 'phase-1-2-accepted-positive' `
        -StatusContent $phase12AcceptedStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH'
    Assert-Positive 'Phase 1.2 accepted with connected runtime' $phase12Accepted

    $phase14AcceptedStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_4_READONLY_DOCUMENT_INSPECTION' `
        -AcceptedCommit 'ffffffffffffffffffffffffffffffffffffffff' `
        -AcceptedRun '999' `
        -WorkBatch 'PHASE_1_5_READONLY_OBJECT_INSPECTION' `
        -NextAction 'IMPLEMENT_PHASE_1_5_READONLY_OBJECT_INSPECTION' `
        -AutoCadRuntime 'CONNECTED' `
        -DwgRead 'IMPLEMENTED'
    $phase14Accepted = New-Fixture `
        -Name 'phase-1-4-accepted-positive' `
        -StatusContent $phase14AcceptedStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_5_READONLY_OBJECT_INSPECTION'
    Assert-Positive 'Phase 1.4 accepted with controlled DWG read' $phase14Accepted

    $earlyDwgReadStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH' `
        -AcceptedCommit 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' `
        -AcceptedRun '789' `
        -WorkBatch 'PHASE_1_4_READONLY_DOCUMENT_INSPECTION' `
        -NextAction 'IMPLEMENT_PHASE_1_4_READONLY_DOCUMENT_INSPECTION' `
        -AutoCadRuntime 'CONNECTED' `
        -DwgRead 'IMPLEMENTED'
    $earlyDwgRead = New-Fixture `
        -Name 'dwg-read-before-phase-1-4-negative' `
        -StatusContent $earlyDwgReadStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_4_READONLY_DOCUMENT_INSPECTION'
    Assert-Negative `
        'DWG read cannot be implemented before Phase 1.4 acceptance' `
        $earlyDwgRead `
        'SAFETY_FACT_PREREQUISITE_MISSING key=dwg_read'

    $acceptedWithoutDwgRead = New-Fixture `
        -Name 'phase-1-4-without-dwg-read-negative' `
        -StatusContent $phase14AcceptedStatus.Replace(
            'dwg_read=IMPLEMENTED',
            'dwg_read=NOT_IMPLEMENTED') `
        -RoadmapAction 'IMPLEMENT_PHASE_1_5_READONLY_OBJECT_INSPECTION'
    Assert-Negative `
        'Phase 1.4 acceptance requires implemented DWG read' `
        $acceptedWithoutDwgRead `
        'ACCEPTED_WORK_BATCH_SAFETY_FACT_MISMATCH.*key=dwg_read'

    $earlyConnectedStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_MAINTENANCE_DEPENDENCIES' `
        -AcceptedCommit 'dddddddddddddddddddddddddddddddddddddddd' `
        -AcceptedRun '456' `
        -WorkBatch 'PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE' `
        -NextAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE' `
        -AutoCadRuntime 'CONNECTED'
    $earlyConnected = New-Fixture `
        -Name 'runtime-connected-before-phase-1-2-negative' `
        -StatusContent $earlyConnectedStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    Assert-Negative `
        'runtime cannot be connected before Phase 1.2 acceptance' `
        $earlyConnected `
        'SAFETY_FACT_PREREQUISITE_MISSING key=autocad_runtime'

    foreach ($case in @(
        @{ Name = 'phase-1-2-dwg-read'; Parameter = 'DwgRead'; Value = 'IMPLEMENTED'; Key = 'dwg_read'; Expected = 'SAFETY_FACT_PREREQUISITE_MISSING key=dwg_read' },
        @{ Name = 'phase-1-2-dwg-write'; Parameter = 'DwgWrite'; Value = 'IMPLEMENTED'; Key = 'dwg_write'; Expected = 'SAFETY_FACT_CONTRADICTION key=dwg_write' },
        @{ Name = 'phase-1-2-allow-write'; Parameter = 'AllowWrite'; Value = 'ENABLED'; Key = 'allow_write'; Expected = 'SAFETY_FACT_CONTRADICTION key=allow_write' },
        @{ Name = 'phase-1-2-allow-script'; Parameter = 'AllowScript'; Value = 'ENABLED'; Key = 'allow_script'; Expected = 'SAFETY_FACT_CONTRADICTION key=allow_script' }
    )) {
        $parameters = @{
            AcceptedBatch = 'PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
            AcceptedCommit = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'
            AcceptedRun = '789'
            WorkBatch = 'PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH'
            NextAction = 'IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH'
            AutoCadRuntime = 'CONNECTED'
        }
        $parameters[$case.Parameter] = $case.Value
        $status = New-Schema2Status @parameters
        $fixture = New-Fixture `
            -Name ($case.Name + '-negative') `
            -StatusContent $status `
            -RoadmapAction 'IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH'
        Assert-Negative `
            "Phase 1.2 rejects unsafe fact $($case.Key)" `
            $fixture `
            $case.Expected
    }

    $skippedMaintenanceStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP' `
        -WorkBatch 'PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE' `
        -NextAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    $skippedMaintenance = New-Fixture `
        -Name 'maintenance-skip-negative' `
        -StatusContent $skippedMaintenanceStatus `
        -RoadmapAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    Assert-Negative `
        'Phase 1.2 cannot skip inserted maintenance' `
        $skippedMaintenance `
        'UNFINISHED_WORK_BATCH_ORDER_INVALID'

    $maintenancePendingUncommittedStatus = $maintenancePendingStatus.Replace(
        'work_batch_commit=PENDING',
        'work_batch_commit=UNCOMMITTED')
    $maintenancePendingUncommitted = New-Fixture `
        -Name 'maintenance-pending-uncommitted-negative' `
        -StatusContent $maintenancePendingUncommittedStatus `
        -RoadmapAction 'PHASE_1_MAINTENANCE_DEPENDENCIES_CI_WAIT_OR_INVESTIGATION'
    Assert-Negative `
        'CI pending requires a concrete or pending commit reference' `
        $maintenancePendingUncommitted `
        'WORK_BATCH_COMMIT_STATE_MISMATCH'

    $missingBlock = New-Fixture `
        -Name 'missing-block' `
        -StatusContent '# no authority block' `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative 'authority block missing' $missingBlock 'AUTHORITY_BLOCK_INVALID'

    $authorityBlock = [regex]::Match(
        $validSchema2,
        '(?s)<!--[ \t]*cad-max-current-authority:start.*?cad-max-current-authority:end[ \t]*-->').Value
    $duplicateBlock = New-Fixture `
        -Name 'duplicate-block' `
        -StatusContent ($validSchema2 + [Environment]::NewLine + $authorityBlock) `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative 'authority block duplicate' $duplicateBlock 'AUTHORITY_BLOCK_INVALID'

    $invalidSchema = New-Fixture `
        -Name 'invalid-schema' `
        -StatusContent $validSchema2.Replace('authority_schema=2', 'authority_schema=99') `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative 'unsupported schema' $invalidSchema 'AUTHORITY_SCHEMA_UNSUPPORTED'

    foreach ($case in @(
        @{ Name = 'allow-write'; From = 'allow_write=DISABLED'; To = 'allow_write=ENABLED'; Expected = 'SAFETY_FACT_CONTRADICTION key=allow_write' },
        @{ Name = 'allow-script'; From = 'allow_script=DISABLED'; To = 'allow_script=ENABLED'; Expected = 'SAFETY_FACT_CONTRADICTION key=allow_script' },
        @{ Name = 'dwg-write'; From = 'dwg_write=NOT_IMPLEMENTED'; To = 'dwg_write=IMPLEMENTED'; Expected = 'SAFETY_FACT_CONTRADICTION key=dwg_write' },
        @{ Name = 'runtime-ready'; From = 'autocad_runtime=NOT_CONNECTED'; To = 'autocad_runtime=READY'; Expected = 'SAFETY_FACT_VALUE_INVALID key=autocad_runtime' }
    )) {
        $fixture = New-Fixture `
            -Name $case.Name `
            -StatusContent $validSchema2.Replace($case.From, $case.To) `
            -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
        Assert-Negative $case.Name $fixture $case.Expected
    }

    $emptyAcceptedCommit = New-Fixture `
        -Name 'accepted-empty-commit' `
        -StatusContent $validSchema2.Replace(
            'accepted_work_batch_commit=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
            'accepted_work_batch_commit=') `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative 'accepted commit empty' $emptyAcceptedCommit 'AUTHORITY_KEY_MISSING key=accepted_work_batch_commit'

    $acceptedCiNotRun = New-Fixture `
        -Name 'accepted-ci-not-run' `
        -StatusContent $validSchema2.Replace(
            'accepted_work_batch_ci_run=123',
            'accepted_work_batch_ci_run=NOT_RUN') `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative 'accepted CI not run' $acceptedCiNotRun 'ACCEPTED_WORK_BATCH_CI_RUN_INVALID'

    $wrongAction = New-Fixture `
        -Name 'wrong-work-action' `
        -StatusContent $validSchema2.Replace(
            'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP',
            'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE') `
        -RoadmapAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    Assert-Negative 'next action work batch mismatch' $wrongAction 'NEXT_ACTION_WORK_BATCH_MISMATCH'

    $sameAcceptedAndUnfinished = New-Fixture `
        -Name 'accepted-equals-unfinished' `
        -StatusContent (New-Schema2Status -AcceptedBatch 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP') `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative `
        'accepted work batch equals unfinished work batch' `
        $sameAcceptedAndUnfinished `
        'UNFINISHED_WORK_BATCH_ORDER_INVALID'

    $invalidEvidence = New-Fixture `
        -Name 'invalid-evidence-name' `
        -StatusContent $validSchema2 `
        -RoadmapAction 'IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP' `
        -InvalidEvidenceName
    Assert-Negative 'evidence attempt name invalid' $invalidEvidence 'EVIDENCE_ATTEMPT_NAME_INVALID'

    $roadmapMismatch = New-Fixture `
        -Name 'roadmap-mismatch' `
        -StatusContent $validSchema2 `
        -RoadmapAction 'IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE'
    Assert-Negative 'ROADMAP next action mismatch' $roadmapMismatch 'ROADMAP_NEXT_ACTION_MISMATCH'

    $previousFailedStatus = New-Schema2Status `
        -WorkStatus 'COMMITTED|CI_FAILED|FIX_REQUIRED' `
        -WorkCommit 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' `
        -WorkRun '456' `
        -NextAction 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP_CI_BLOCKER_FIX'
    $previousFailed = New-Fixture `
        -Name 'transition-previous-failed' `
        -StatusContent $previousFailedStatus `
        -RoadmapAction 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP_CI_BLOCKER_FIX'
    $directAcceptedStatus = New-Schema2Status `
        -AcceptedBatch 'PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP' `
        -AcceptedCommit 'cccccccccccccccccccccccccccccccccccccccc' `
        -AcceptedRun '789' `
        -WorkStatus 'ACCEPTED|CI_GREEN' `
        -WorkCommit 'cccccccccccccccccccccccccccccccccccccccc' `
        -WorkRun '789' `
        -NextAction 'ADVANCE_AFTER_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    $directAccepted = New-Fixture `
        -Name 'transition-direct-accepted' `
        -StatusContent $directAcceptedStatus `
        -RoadmapAction 'ADVANCE_AFTER_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP'
    Assert-Negative `
        'CI failed cannot transition directly to accepted' `
        $directAccepted `
        'WORK_STATUS_TRANSITION_INVALID' `
        $previousFailed.Status

    Write-Output 'PASS / CURRENT_AUTHORITY_REGRESSION'
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
