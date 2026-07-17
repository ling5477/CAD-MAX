# Contributing to CAD-MAX

## Branches

The default integration and day-to-day development branch is dev. The main branch is
the stable release line. Create focused branches from dev for non-trivial work.

## Required checks

Before opening a pull request:

    .\scripts\verify.ps1

Documentation-only changes must at least run:

    .\scripts\docs\verify-docs.ps1

Do not skip failing checks, use continue-on-error, or remove a valid assertion to make
CI green.

## Current authority and docs budget

- Read `docs/current/STATUS.md` before changing code or documentation.
- Ordinary code tasks do not update docs by default; if evidence is needed, append at
  most one factual entry to `docs/current/WORKLOG.md`.
- Validation baseline changes may append `TESTING.md` and `WORKLOG.md`.
- Only a Phase/next-action transition updates current `STATUS.md` and `ROADMAP.md`.
- Never rewrite a failed ledger entry as passed; append the remediation and rerun.
- CI is green only when the run for the exact `origin/dev` HEAD succeeds.

## Scope discipline

- Register an MCP tool only after its real implementation and failure tests exist.
- Keep Python MCP, language-neutral contracts, bridge core, and AutoCAD execution in
  separate modules.
- Preserve the common schemaVersion/requestId/traceId envelope.
- Map internal exceptions to structured errors without exposing paths or stack traces.
- Add normal, failure, and boundary tests for changed core behavior.
- Do not commit Autodesk DLLs, SDK files, credentials, .env, generated output, or IDE
  private state.

## Commit messages

Use Conventional Commits. Examples:

    feat(bridge): 增加只读图纸状态处理器
    test(security): 补充路径穿越回归测试
    docs(architecture): 记录文档上下文执行边界

## Adding an AutoCAD command

A command change must include:

1. A versioned contract and validation rules.
2. A C# handler with cancellation and timeout behavior.
3. Correct AutoCAD application/document context marshaling.
4. Structured exception mapping and non-sensitive logs.
5. Failure, cancellation, and regression tests.
6. An explicit read/write classification.
7. Updated capabilities and documentation.
