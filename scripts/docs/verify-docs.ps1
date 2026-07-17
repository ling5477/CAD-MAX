<#
.SYNOPSIS
运行 CAD-MAX 文档治理回归、current authority 与 Markdown link checks。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$powerShellExecutable = (Get-Process -Id $PID).Path

function Invoke-CheckedScript {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Path
    )

    Write-Host "==> $Name"
    & $powerShellExecutable -NoLogo -NoProfile -File $Path
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Invoke-CheckedScript `
    -Name 'current authority regression' `
    -Path (Join-Path $PSScriptRoot 'test-current-authority.ps1')
Invoke-CheckedScript `
    -Name 'current authority' `
    -Path (Join-Path $PSScriptRoot 'check-current-authority.ps1')
Invoke-CheckedScript `
    -Name 'documentation links' `
    -Path (Join-Path $PSScriptRoot 'check-doc-links.ps1')

Write-Output 'PASS / DOCUMENTATION_GOVERNANCE_VALID'
