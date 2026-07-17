<#
.SYNOPSIS
只读校验仓库治理与技术文档中的 Markdown 相对文件链接。
.NOTES
外部 URL、页内 anchor 与 fenced code 会被忽略；指向仓库外的链接 fail closed。
#>
[CmdletBinding()]
param(
    [string[]] $Roots = @(
        'README.md',
        'AGENTS.md',
        'CONTRIBUTING.md',
        'SECURITY.md',
        'docs',
        'contracts'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$rootPrefix = $repositoryRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$checked = 0
$warnings = 0
$errors = 0

function Write-LinkFinding {
    param(
        [ValidateSet('WARNING', 'ERROR')][string] $Level,
        [string] $File,
        [int] $Line,
        [string] $Link
    )

    if ($Level -eq 'ERROR') {
        $script:errors++
    }
    else {
        $script:warnings++
    }
    Write-Output ("{0} {1}:{2} -> {3}" -f $Level, $File, $Line, $Link)
}

foreach ($rootInput in $Roots) {
    $rootPath = if ([System.IO.Path]::IsPathRooted($rootInput)) {
        [System.IO.Path]::GetFullPath($rootInput)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $rootInput))
    }

    if (-not $rootPath.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        -not $rootPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Write-LinkFinding -Level ERROR -File $rootInput -Line 0 -Link 'ROOT_OUTSIDE_REPOSITORY'
        continue
    }
    if (-not (Test-Path -LiteralPath $rootPath)) {
        Write-LinkFinding -Level ERROR -File $rootInput -Line 0 -Link 'ROOT_NOT_FOUND'
        continue
    }

    $item = Get-Item -LiteralPath $rootPath
    $files = if ($item.PSIsContainer) {
        @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Filter '*.md')
    }
    else {
        @($item)
    }

    foreach ($file in $files) {
        $relativeFile = $file.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
        $inFence = $false
        $lineNumber = 0

        foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
            $lineNumber++
            if ($line -match '^\s*(```|~~~)') {
                $inFence = -not $inFence
                continue
            }
            if ($inFence) {
                continue
            }

            foreach ($match in [regex]::Matches($line, '\[[^\]]*\]\(([^)]+)\)')) {
                $rawLink = $match.Groups[1].Value.Trim().Trim('<', '>')
                if ([string]::IsNullOrWhiteSpace($rawLink) -or
                    $rawLink -match '^(https?://|mailto:|javascript:|#)') {
                    continue
                }

                $linkWithoutAnchor = $rawLink.Split('#')[0]
                if ([string]::IsNullOrWhiteSpace($linkWithoutAnchor)) {
                    continue
                }

                $checked++
                if ($linkWithoutAnchor -match '[*?]') {
                    Write-LinkFinding -Level WARNING -File $relativeFile -Line $lineNumber -Link $rawLink
                    continue
                }

                try {
                    $decoded = [uri]::UnescapeDataString($linkWithoutAnchor)
                    $target = if ($decoded.StartsWith('/')) {
                        Join-Path $repositoryRoot $decoded.TrimStart('/')
                    }
                    else {
                        Join-Path $file.DirectoryName $decoded
                    }
                    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
                }
                catch {
                    Write-LinkFinding -Level ERROR -File $relativeFile -Line $lineNumber -Link $rawLink
                    continue
                }

                if (-not $resolvedTarget.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -and
                    -not $resolvedTarget.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    Write-LinkFinding -Level ERROR -File $relativeFile -Line $lineNumber -Link $rawLink
                    continue
                }
                if (Test-Path -LiteralPath $resolvedTarget) {
                    continue
                }

                $isEvidenceLedger = $relativeFile -in @(
                    'docs/current/TESTING.md',
                    'docs/current/WORKLOG.md'
                )
                $level = if ($isEvidenceLedger) { 'WARNING' } else { 'ERROR' }
                Write-LinkFinding -Level $level -File $relativeFile -Line $lineNumber -Link $rawLink
            }
        }
    }
}

Write-Output ("LINK_CHECK checked={0} warnings={1} errors={2}" -f $checked, $warnings, $errors)
if ($errors -gt 0) {
    Write-Output 'BLOCKED / DOC_LINK_BROKEN'
    throw 'Documentation link verification failed.'
}

Write-Output 'PASS / DOC_LINKS_VALID'
