<#
.SYNOPSIS
回归 current authority checker 的真实正例与安全事实篡改负例。
.NOTES
负例 fixture 只写入 Git 忽略的 .tools/governance-tests/。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$checkerPath = Join-Path $PSScriptRoot 'check-current-authority.ps1'
$statusPath = Join-Path $repositoryRoot 'docs\current\STATUS.md'
$fixtureRoot = Join-Path $repositoryRoot '.tools\governance-tests'
$invalidStatusPath = Join-Path $fixtureRoot 'status-invalid-safety.md'
$powerShellExecutable = (Get-Process -Id $PID).Path

function Invoke-AuthorityChecker {
    param([Parameter(Mandatory)][string] $Path)

    $output = @(& $powerShellExecutable -NoLogo -NoProfile -File $checkerPath -StatusPath $Path 2>&1)
    $exitCode = $LASTEXITCODE
    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = ($output -join [Environment]::NewLine)
    }
}

$positive = Invoke-AuthorityChecker $statusPath
if ($positive.ExitCode -ne 0 -or $positive.Text -notmatch 'PASS / CURRENT_AUTHORITY_CONSISTENT') {
    throw "Positive authority regression failed:`n$($positive.Text)"
}

New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
$validContent = [System.IO.File]::ReadAllText(
    $statusPath,
    (New-Object System.Text.UTF8Encoding($false)))
$invalidContent = $validContent.Replace('dwg_write=NOT_IMPLEMENTED', 'dwg_write=IMPLEMENTED')
if ($invalidContent -eq $validContent) {
    throw 'Negative authority fixture replacement did not change the source content.'
}
[System.IO.File]::WriteAllText(
    $invalidStatusPath,
    $invalidContent,
    (New-Object System.Text.UTF8Encoding($false)))

$negative = Invoke-AuthorityChecker $invalidStatusPath
if ($negative.ExitCode -eq 0 -or
    $negative.Text -notmatch 'SAFETY_FACT_CONTRADICTION key=dwg_write' -or
    $negative.Text -notmatch 'BLOCKED / CURRENT_AUTHORITY_CONFLICT') {
    throw "Negative authority regression failed:`n$($negative.Text)"
}

Write-Output 'PASS / CURRENT_AUTHORITY_REGRESSION'
