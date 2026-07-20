<#
.SYNOPSIS
从 machine contract 校验 CAD-MAX current authority、work-batch、evidence 与 ROADMAP 一致性。
.NOTES
本脚本只读取本地仓库；不访问 Git、GitHub、网络或 AutoCAD，也不修改任何文件。
#>
[CmdletBinding()]
param(
    [string] $StatusPath = 'docs/current/STATUS.md',
    [string] $RoadmapPath = 'docs/current/ROADMAP.md',
    [string] $EvidenceRoot = '',
    [string] $PreviousStatusPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$errors = New-Object System.Collections.Generic.List[string]
$blockers = New-Object System.Collections.Generic.List[string]

function Add-AuthorityError {
    param(
        [Parameter(Mandatory)][string] $Message,
        [string] $Blocker = 'CURRENT_AUTHORITY_CONFLICT'
    )

    $script:errors.Add($Message)
    if (-not $script:blockers.Contains($Blocker)) {
        $script:blockers.Add($Blocker)
    }
    Write-Host ("ERROR {0}" -f $Message)
}

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string] $Path)

    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    }

    $rootPrefix = $repositoryRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        -not $candidate.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must remain inside the repository: $Path"
    }

    return $candidate
}

function Read-Utf8File {
    param([Parameter(Mandatory)][string] $Path)

    return [System.IO.File]::ReadAllText(
        $Path,
        (New-Object System.Text.UTF8Encoding($false)))
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)][string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Read-AuthorityBlock {
    param(
        [Parameter(Mandatory)][string] $Content,
        [Parameter(Mandatory)][string] $Label
    )

    $matches = [regex]::Matches(
        $Content,
        '(?s)<!--[ \t]*cad-max-current-authority:start[ \t]*\r?\n(?<body>.*?)\r?\ncad-max-current-authority:end[ \t]*-->')
    if ($matches.Count -ne 1) {
        Add-AuthorityError "AUTHORITY_BLOCK_INVALID label=$Label expected=1 actual=$($matches.Count)"
        return $null
    }

    $authority = @{}
    foreach ($line in ($matches[0].Groups['body'].Value -split '\r?\n')) {
        $lineMatch = [regex]::Match(
            $line,
            '^(?<key>[a-z][a-z0-9_]*)=(?<value>[A-Z0-9a-f_|.-]*)$')
        if (-not $lineMatch.Success) {
            Add-AuthorityError "AUTHORITY_LINE_INVALID label=$Label value=$line"
            continue
        }

        $key = $lineMatch.Groups['key'].Value
        if ($authority.ContainsKey($key)) {
            Add-AuthorityError "AUTHORITY_KEY_DUPLICATE label=$Label key=$key"
            continue
        }
        $authority[$key] = $lineMatch.Groups['value'].Value
    }

    return $authority
}

function Test-BodyStatusLine {
    param(
        [Parameter(Mandatory)][string] $Content,
        [Parameter(Mandatory)][string] $Subject,
        [Parameter(Mandatory)][string] $StatusPattern
    )

    $pattern = '(?im)^\s*-\s*{0}\s*(?:\x3a|\uff1a)[^\r\n]*{1}' -f
        [regex]::Escape($Subject),
        $StatusPattern
    return $Content -match $pattern
}

$contractPath = Join-Path $PSScriptRoot 'governance-workflow-contract.json'
$contract = $null
try {
    $contract = Read-Utf8File $contractPath | ConvertFrom-Json -Depth 32
}
catch {
    Add-AuthorityError "GOVERNANCE_CONTRACT_INVALID type=$($_.Exception.GetType().Name)" 'CONTRACT_MISMATCH'
}

$resolvedStatus = Resolve-RepositoryPath $StatusPath
$statusContent = ''
$authority = $null
if (-not (Test-Path -LiteralPath $resolvedStatus -PathType Leaf)) {
    Add-AuthorityError "STATUS_NOT_FOUND path=$StatusPath"
}
else {
    $statusContent = Read-Utf8File $resolvedStatus
    $authority = Read-AuthorityBlock -Content $statusContent -Label 'current'
}

$schemaSpec = $null
if ($null -ne $contract -and $null -ne $authority) {
    if (-not $authority.ContainsKey('authority_schema')) {
        Add-AuthorityError 'AUTHORITY_KEY_MISSING key=authority_schema'
    }
    else {
        $schemaSpec = Get-ObjectPropertyValue $contract.authoritySchemas $authority.authority_schema
        if ($null -eq $schemaSpec) {
            Add-AuthorityError (
                "AUTHORITY_SCHEMA_UNSUPPORTED actual={0}" -f $authority.authority_schema) 'CONTRACT_MISMATCH'
        }
    }
}

if ($null -ne $schemaSpec -and $null -ne $authority) {
    $requiredKeys = @($schemaSpec.requiredKeys)
    foreach ($key in $requiredKeys) {
        if (-not $authority.ContainsKey($key) -or
            [string]::IsNullOrWhiteSpace([string]$authority[$key])) {
            Add-AuthorityError "AUTHORITY_KEY_MISSING key=$key"
        }
    }
    foreach ($key in @($authority.Keys)) {
        if ($key -notin $requiredKeys) {
            Add-AuthorityError "AUTHORITY_KEY_UNKNOWN key=$key"
        }
    }

    foreach ($factProperty in $contract.fixedSecurityFacts.PSObject.Properties) {
        $key = $factProperty.Name
        $expected = [string]$factProperty.Value
        if ($authority.ContainsKey($key) -and $authority[$key] -ne $expected) {
            Add-AuthorityError (
                "SAFETY_FACT_CONTRADICTION key={0} expected={1} actual={2}" -f
                $key,
                $expected,
                $authority[$key]) 'SECURITY_DEFAULT_REGRESSION'
        }
    }

    foreach ($factProperty in $contract.conditionalSecurityFacts.PSObject.Properties) {
        $key = $factProperty.Name
        $policy = $factProperty.Value
        if (-not $authority.ContainsKey($key)) {
            continue
        }

        $actual = [string]$authority[$key]
        if ($actual -notin @($policy.allowedValues)) {
            Add-AuthorityError (
                "SAFETY_FACT_VALUE_INVALID key={0} actual={1}" -f
                $key,
                $actual) 'SECURITY_DEFAULT_REGRESSION'
            continue
        }

        $minimumAcceptedBatch = Get-ObjectPropertyValue `
            $policy.minimumAcceptedWorkBatchByValue `
            $actual
        if ($null -ne $minimumAcceptedBatch) {
            $workBatches = @($schemaSpec.workBatches)
            $minimumIndex = [Array]::IndexOf(
                [object[]]$workBatches,
                [object][string]$minimumAcceptedBatch)
            $acceptedIndex = if ($authority.ContainsKey('accepted_work_batch')) {
                [Array]::IndexOf(
                    [object[]]$workBatches,
                    [object]$authority.accepted_work_batch)
            }
            else {
                -1
            }
            if ($minimumIndex -lt 0) {
                Add-AuthorityError (
                    "SECURITY_FACT_POLICY_BATCH_INVALID key={0} value={1} batch={2}" -f
                    $key,
                    $actual,
                    $minimumAcceptedBatch) 'CONTRACT_MISMATCH'
            }
            elseif ($acceptedIndex -lt $minimumIndex) {
                Add-AuthorityError (
                    "SAFETY_FACT_PREREQUISITE_MISSING key={0} value={1} minimum_accepted_batch={2}" -f
                    $key,
                    $actual,
                    $minimumAcceptedBatch) 'SECURITY_DEFAULT_REGRESSION'
            }
        }
    }

    if ($authority.authority_schema -eq '1') {
        if ($authority.current_phase -ne [string]$schemaSpec.currentPhase) {
            Add-AuthorityError "CURRENT_PHASE_UNSUPPORTED actual=$($authority.current_phase)"
        }
        if ($authority.next_phase -ne [string]$schemaSpec.nextPhase) {
            Add-AuthorityError "NEXT_PHASE_INVALID actual=$($authority.next_phase)"
        }

        $expectedNextAction = Get-ObjectPropertyValue `
            $schemaSpec.phaseStatusToNextAction `
            $authority.phase_status
        if ($null -eq $expectedNextAction) {
            Add-AuthorityError "PHASE_STATUS_INVALID actual=$($authority.phase_status)"
        }
        elseif ($authority.next_action -ne [string]$expectedNextAction) {
            Add-AuthorityError (
                "NEXT_ACTION_MISMATCH expected={0} actual={1}" -f
                $expectedNextAction,
                $authority.next_action) 'NEXT_ACTION_MISMATCH'
        }
    }
    elseif ($authority.authority_schema -eq '2') {
        if ($authority.current_phase -ne [string]$schemaSpec.currentPhase) {
            Add-AuthorityError "CURRENT_PHASE_UNSUPPORTED actual=$($authority.current_phase)"
        }
        if ($authority.phase_status -ne [string]$schemaSpec.phaseStatus) {
            Add-AuthorityError "PHASE_STATUS_INVALID actual=$($authority.phase_status)"
        }
        if ($authority.accepted_work_batch_status -ne [string]$schemaSpec.acceptedWorkBatchStatus) {
            Add-AuthorityError (
                "ACCEPTED_WORK_BATCH_STATUS_INVALID actual={0}" -f
                $authority.accepted_work_batch_status)
        }
        if ($authority.accepted_work_batch_commit -notmatch '^[0-9a-f]{40}$') {
            Add-AuthorityError (
                "ACCEPTED_WORK_BATCH_COMMIT_INVALID value={0}" -f
                $authority.accepted_work_batch_commit)
        }
        if ($authority.accepted_work_batch_ci_run -notmatch '^[1-9][0-9]*$') {
            Add-AuthorityError (
                "ACCEPTED_WORK_BATCH_CI_RUN_INVALID value={0}" -f
                $authority.accepted_work_batch_ci_run)
        }

        $workBatches = @($schemaSpec.workBatches)
        $acceptedIndex = [Array]::IndexOf(
            [object[]]$workBatches,
            [object]$authority.accepted_work_batch)
        $workIndex = [Array]::IndexOf(
            [object[]]$workBatches,
            [object]$authority.work_batch)
        if ($acceptedIndex -lt 0) {
            Add-AuthorityError "ACCEPTED_WORK_BATCH_INVALID value=$($authority.accepted_work_batch)"
        }
        if ($workIndex -lt 0) {
            Add-AuthorityError "WORK_BATCH_INVALID value=$($authority.work_batch)"
        }

        $acceptedSecurityFacts = Get-ObjectPropertyValue `
            $contract.acceptedWorkBatchSecurityFacts `
            $authority.accepted_work_batch
        if ($null -ne $acceptedSecurityFacts) {
            foreach ($factProperty in $acceptedSecurityFacts.PSObject.Properties) {
                $key = $factProperty.Name
                $expected = [string]$factProperty.Value
                if ($authority.ContainsKey($key) -and $authority[$key] -ne $expected) {
                    Add-AuthorityError (
                        "ACCEPTED_WORK_BATCH_SAFETY_FACT_MISMATCH batch={0} key={1} expected={2} actual={3}" -f
                        $authority.accepted_work_batch,
                        $key,
                        $expected,
                        $authority[$key]) 'SECURITY_DEFAULT_REGRESSION'
                }
            }
        }

        if ($authority.work_batch_status -notin @($contract.workBatchStatuses)) {
            Add-AuthorityError "WORK_BATCH_STATUS_INVALID value=$($authority.work_batch_status)"
        }
        else {
            $expectedNextActionType = Get-ObjectPropertyValue `
                $contract.nextActionTypes `
                $authority.work_batch_status
            if ($null -eq $expectedNextActionType) {
                Add-AuthorityError (
                    "NEXT_ACTION_TYPE_MISSING status={0}" -f
                    $authority.work_batch_status) 'CONTRACT_MISMATCH'
            }
            $fieldPolicy = Get-ObjectPropertyValue `
                $contract.workStatusFieldPolicies `
                $authority.work_batch_status
            if ($null -eq $fieldPolicy) {
                Add-AuthorityError (
                    "WORK_BATCH_STATUS_POLICY_MISSING status={0}" -f
                    $authority.work_batch_status) 'CONTRACT_MISMATCH'
            }
            else {
                if ($authority.work_batch_commit -notmatch [string]$fieldPolicy.commitPattern) {
                    Add-AuthorityError (
                        "WORK_BATCH_COMMIT_STATE_MISMATCH status={0} commit={1}" -f
                        $authority.work_batch_status,
                        $authority.work_batch_commit)
                }
                if ($authority.work_batch_ci_run -notmatch [string]$fieldPolicy.ciRunPattern) {
                    Add-AuthorityError (
                        "WORK_BATCH_CI_STATE_MISMATCH status={0} ci_run={1}" -f
                        $authority.work_batch_status,
                        $authority.work_batch_ci_run)
                }

                $expectedNextAction = ([string]$fieldPolicy.nextActionTemplate).Replace(
                    '{workBatch}',
                    $authority.work_batch)
                if ($authority.next_action -ne $expectedNextAction) {
                    Add-AuthorityError (
                        "NEXT_ACTION_WORK_BATCH_MISMATCH work_batch={0} expected={1} actual={2}" -f
                        $authority.work_batch,
                        $expectedNextAction,
                        $authority.next_action) 'NEXT_ACTION_MISMATCH'
                }
            }
        }

        if ($acceptedIndex -ge 0 -and $workIndex -ge 0) {
            if ($authority.work_batch_status -eq [string]$schemaSpec.acceptedWorkBatchStatus) {
                if ($acceptedIndex -ne $workIndex) {
                    Add-AuthorityError (
                        "ACCEPTED_WORK_BATCH_SYNC_MISMATCH accepted={0} work={1}" -f
                        $authority.accepted_work_batch,
                        $authority.work_batch)
                }
                if ($authority.accepted_work_batch_commit -ne $authority.work_batch_commit -or
                    $authority.accepted_work_batch_ci_run -ne $authority.work_batch_ci_run) {
                    Add-AuthorityError 'ACCEPTED_WORK_BATCH_EVIDENCE_MISMATCH'
                }
            }
            elseif ($acceptedIndex -ne ($workIndex - 1)) {
                Add-AuthorityError (
                    "UNFINISHED_WORK_BATCH_ORDER_INVALID accepted={0} work={1}" -f
                    $authority.accepted_work_batch,
                    $authority.work_batch)
            }
        }

        $statusBody = [regex]::Replace(
            $statusContent,
            '(?s)<!--[ \t]*cad-max-current-authority:start.*?cad-max-current-authority:end[ \t]*-->',
            '')
        if (-not (Test-BodyStatusLine $statusBody 'Phase 1' 'IN\s*PROGRESS\s*/\s*NOT\s*FROZEN')) {
            Add-AuthorityError 'PHASE_STATUS_BODY_CONTRADICTION phase=PHASE_1'
        }
        if (-not (Test-BodyStatusLine $statusBody $authority.accepted_work_batch 'ACCEPTED\s*/\s*CI\s*GREEN')) {
            Add-AuthorityError (
                "ACCEPTED_WORK_BATCH_BODY_CONTRADICTION batch={0}" -f
                $authority.accepted_work_batch)
        }
        if (-not $statusBody.Contains($authority.work_batch_status.Replace('|', ' / '))) {
            Add-AuthorityError (
                "WORK_BATCH_BODY_CONTRADICTION batch={0} status={1}" -f
                $authority.work_batch,
                $authority.work_batch_status)
        }
    }

    if (-not $statusContent.Contains($authority.next_action)) {
        Add-AuthorityError (
            "NEXT_ACTION_BODY_MISMATCH expected={0}" -f
            $authority.next_action) 'NEXT_ACTION_MISMATCH'
    }

    $resolvedRoadmap = Resolve-RepositoryPath $RoadmapPath
    if (-not (Test-Path -LiteralPath $resolvedRoadmap -PathType Leaf)) {
        Add-AuthorityError "ROADMAP_NOT_FOUND path=$RoadmapPath"
    }
    else {
        $roadmapContent = Read-Utf8File $resolvedRoadmap
        if (-not $roadmapContent.Contains($authority.next_action)) {
            Add-AuthorityError (
                "ROADMAP_NEXT_ACTION_MISMATCH expected={0} path={1}" -f
                $authority.next_action,
                $RoadmapPath) 'NEXT_ACTION_MISMATCH'
        }
    }
}

if ($null -ne $contract) {
    $effectiveEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
        [string]$contract.evidence.root
    }
    else {
        $EvidenceRoot
    }
    $resolvedEvidenceRoot = Resolve-RepositoryPath $effectiveEvidenceRoot
    if (-not (Test-Path -LiteralPath $resolvedEvidenceRoot -PathType Container)) {
        Add-AuthorityError "EVIDENCE_ROOT_NOT_FOUND path=$effectiveEvidenceRoot" 'EVIDENCE_INVALID'
    }
    else {
        $nestedDirectories = @(Get-ChildItem -LiteralPath $resolvedEvidenceRoot -Directory -Recurse)
        if ([bool]$contract.evidence.rejectNestedDirectories -and $nestedDirectories.Count -gt 0) {
            Add-AuthorityError 'EVIDENCE_NESTED_DIRECTORY_INVALID' 'EVIDENCE_INVALID'
        }

        $evidenceFiles = @(Get-ChildItem -LiteralPath $resolvedEvidenceRoot -File)
        $indexFile = @($evidenceFiles | Where-Object { $_.Name -ceq [string]$contract.evidence.indexFileName })
        if ($indexFile.Count -ne 1) {
            Add-AuthorityError "EVIDENCE_INDEX_INVALID expected=1 actual=$($indexFile.Count)" 'EVIDENCE_INVALID'
        }
        elseif (((Read-Utf8File $indexFile[0].FullName) -replace '\s', '').Length -lt
            [int]$contract.evidence.minimumIndexNonWhitespaceCharacters) {
            Add-AuthorityError 'EVIDENCE_INDEX_EMPTY' 'EVIDENCE_INVALID'
        }

        foreach ($file in $evidenceFiles) {
            if ($file.Name -ceq [string]$contract.evidence.indexFileName) {
                continue
            }
            if ($file.Name -notmatch [string]$contract.evidence.attemptPattern) {
                Add-AuthorityError "EVIDENCE_ATTEMPT_NAME_INVALID file=$($file.Name)" 'EVIDENCE_INVALID'
                continue
            }
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Add-AuthorityError "EVIDENCE_REPARSE_POINT_INVALID file=$($file.Name)" 'EVIDENCE_INVALID'
            }
            if (((Read-Utf8File $file.FullName) -replace '\s', '').Length -lt
                [int]$contract.evidence.minimumAttemptNonWhitespaceCharacters) {
                Add-AuthorityError "EVIDENCE_ATTEMPT_EMPTY file=$($file.Name)" 'EVIDENCE_INVALID'
            }
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($PreviousStatusPath) -and
    $null -ne $contract -and
    $null -ne $authority) {
    $resolvedPrevious = Resolve-RepositoryPath $PreviousStatusPath
    if (-not (Test-Path -LiteralPath $resolvedPrevious -PathType Leaf)) {
        Add-AuthorityError "PREVIOUS_STATUS_NOT_FOUND path=$PreviousStatusPath"
    }
    else {
        $previousAuthority = Read-AuthorityBlock `
            -Content (Read-Utf8File $resolvedPrevious) `
            -Label 'previous'
        if ($null -ne $previousAuthority -and
            $previousAuthority.ContainsKey('authority_schema') -and
            $previousAuthority.authority_schema -eq '2' -and
            $authority.authority_schema -eq '2' -and
            $previousAuthority.ContainsKey('work_batch') -and
            $previousAuthority.ContainsKey('work_batch_status') -and
            $previousAuthority.work_batch -eq $authority.work_batch) {
            $transition = "{0}->{1}" -f
                $previousAuthority.work_batch_status,
                $authority.work_batch_status
            $allowedTransitions = @($contract.allowedTransitions.ordinary) +
                @($contract.allowedTransitions.highRisk)
            if ($transition -notin $allowedTransitions) {
                Add-AuthorityError "WORK_STATUS_TRANSITION_INVALID transition=$transition"
            }
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Output ("AUTHORITY_CHECK errors={0}" -f $errors.Count)
    Write-Output ("BLOCKED / {0}" -f $blockers[0])
    exit 1
}

if ($null -ne $authority) {
    if ($authority.authority_schema -eq '1') {
        Write-Output (
            "AUTHORITY schema=1 phase={0} phase_status={1} next_action={2}" -f
            $authority.current_phase,
            $authority.phase_status,
            $authority.next_action)
    }
    else {
        Write-Output (
            "AUTHORITY schema=2 phase={0} phase_status={1} accepted_work_batch={2} work_batch={3} work_status={4} next_action={5}" -f
            $authority.current_phase,
            $authority.phase_status,
            $authority.accepted_work_batch,
            $authority.work_batch,
            $authority.work_batch_status,
            $authority.next_action)
    }
}
Write-Output 'AUTHORITY_CHECK errors=0'
Write-Output 'PASS / CURRENT_AUTHORITY_CONSISTENT'
exit 0
