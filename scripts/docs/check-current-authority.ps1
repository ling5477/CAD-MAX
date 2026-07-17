<#
.SYNOPSIS
只读校验 CAD-MAX current authority schema、Phase/next-action 组合和 Phase 0 安全事实。
.NOTES
本脚本不访问 Git、GitHub、网络或 AutoCAD，也不修改任何仓库文件。
#>
[CmdletBinding()]
param(
    [string] $StatusPath = 'docs/current/STATUS.md'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$errors = New-Object System.Collections.Generic.List[string]

function Add-AuthorityError {
    param([Parameter(Mandatory)][string] $Message)

    $script:errors.Add($Message)
    Write-Output ("ERROR {0}" -f $Message)
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
        throw "StatusPath must remain inside the repository: $Path"
    }

    return $candidate
}

$resolvedStatus = Resolve-RepositoryPath $StatusPath
if (-not (Test-Path -LiteralPath $resolvedStatus -PathType Leaf)) {
    Add-AuthorityError "STATUS_NOT_FOUND path=$StatusPath"
}
else {
    $statusContent = [System.IO.File]::ReadAllText(
        $resolvedStatus,
        (New-Object System.Text.UTF8Encoding($false)))
    $authorityMatches = [regex]::Matches(
        $statusContent,
        '(?s)<!--[ \t]*cad-max-current-authority:start[ \t]*\r?\n(?<body>.*?)\r?\ncad-max-current-authority:end[ \t]*-->')

    if ($authorityMatches.Count -ne 1) {
        Add-AuthorityError "AUTHORITY_BLOCK_INVALID expected=1 actual=$($authorityMatches.Count)"
    }
    else {
        $authority = @{}
        foreach ($line in ($authorityMatches[0].Groups['body'].Value -split '\r?\n')) {
            $lineMatch = [regex]::Match(
                $line,
                '^(?<key>[a-z][a-z0-9_]*)=(?<value>[A-Z0-9_|.-]+)$')
            if (-not $lineMatch.Success) {
                Add-AuthorityError "AUTHORITY_LINE_INVALID value=$line"
                continue
            }

            $key = $lineMatch.Groups['key'].Value
            if ($authority.ContainsKey($key)) {
                Add-AuthorityError "AUTHORITY_KEY_DUPLICATE key=$key"
                continue
            }
            $authority[$key] = $lineMatch.Groups['value'].Value
        }

        $requiredKeys = @(
            'authority_schema',
            'current_phase',
            'phase_status',
            'next_phase',
            'next_action',
            'autocad_runtime',
            'dwg_read',
            'dwg_write',
            'read_only',
            'allow_write',
            'allow_script',
            'http_binding'
        )

        foreach ($key in $requiredKeys) {
            if (-not $authority.ContainsKey($key)) {
                Add-AuthorityError "AUTHORITY_KEY_MISSING key=$key"
            }
        }
        foreach ($key in @($authority.Keys)) {
            if ($key -notin $requiredKeys) {
                Add-AuthorityError "AUTHORITY_KEY_UNKNOWN key=$key"
            }
        }

        $hasRequiredKeys = @($requiredKeys | Where-Object { -not $authority.ContainsKey($_) }).Count -eq 0
        if ($hasRequiredKeys) {
            if ($authority.authority_schema -ne '1') {
                Add-AuthorityError "AUTHORITY_SCHEMA_UNSUPPORTED expected=1 actual=$($authority.authority_schema)"
            }
            if ($authority.current_phase -ne 'PHASE_0') {
                Add-AuthorityError "CURRENT_PHASE_UNSUPPORTED expected=PHASE_0 actual=$($authority.current_phase)"
            }
            if ($authority.next_phase -ne 'PHASE_1') {
                Add-AuthorityError "NEXT_PHASE_INVALID expected=PHASE_1 actual=$($authority.next_phase)"
            }

            $allowedStatuses = @('IN_PROGRESS', 'COMPLETED')
            if ($authority.phase_status -notin $allowedStatuses) {
                Add-AuthorityError "PHASE_STATUS_INVALID actual=$($authority.phase_status)"
            }
            else {
                $expectedNextAction = if ($authority.phase_status -eq 'IN_PROGRESS') {
                    'COMPLETE_BOOTSTRAP_VALIDATION'
                }
                else {
                    'PLAN_PHASE_1_AUTOCAD_CONNECTION'
                }
                if ($authority.next_action -ne $expectedNextAction) {
                    Add-AuthorityError "NEXT_ACTION_MISMATCH expected=$expectedNextAction actual=$($authority.next_action)"
                }
            }

            $safetyFacts = [ordered]@{
                autocad_runtime = 'NOT_CONNECTED'
                dwg_read = 'NOT_IMPLEMENTED'
                dwg_write = 'NOT_IMPLEMENTED'
                read_only = 'ENABLED'
                allow_write = 'DISABLED'
                allow_script = 'DISABLED'
                http_binding = 'LOOPBACK_ONLY'
            }
            foreach ($entry in $safetyFacts.GetEnumerator()) {
                if ($authority[$entry.Key] -ne $entry.Value) {
                    Add-AuthorityError (
                        "SAFETY_FACT_CONTRADICTION key={0} expected={1} actual={2}" -f
                        $entry.Key,
                        $entry.Value,
                        $authority[$entry.Key])
                }
            }

            $statusBody = [regex]::Replace(
                $statusContent,
                '(?s)<!--[ \t]*cad-max-current-authority:start.*?cad-max-current-authority:end[ \t]*-->',
                '')
            if (-not $statusBody.Contains($authority.next_action)) {
                Add-AuthorityError "NEXT_ACTION_BODY_MISMATCH expected=$($authority.next_action)"
            }

            Write-Output (
                "AUTHORITY schema={0} phase={1} phase_status={2} next_phase={3} next_action={4}" -f
                $authority.authority_schema,
                $authority.current_phase,
                $authority.phase_status,
                $authority.next_phase,
                $authority.next_action)
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Output ("AUTHORITY_CHECK errors={0}" -f $errors.Count)
    Write-Output 'BLOCKED / CURRENT_AUTHORITY_CONFLICT'
    throw 'Current authority validation failed.'
}

Write-Output 'AUTHORITY_CHECK errors=0'
Write-Output 'PASS / CURRENT_AUTHORITY_CONSISTENT'
