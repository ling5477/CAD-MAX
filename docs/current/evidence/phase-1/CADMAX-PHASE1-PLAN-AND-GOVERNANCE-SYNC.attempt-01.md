# CADMAX-PHASE1-PLAN-AND-GOVERNANCE-SYNC / attempt-01

## Task classification

`ARCHITECTURE / DOCUMENTATION / DOCS_GOVERNANCE / CI_CD / SECURITY_BOUNDARY / GITHUB_PUBLISH`

Risk classification：高风险治理与 CI/CD 发布任务，必须独立 review。

## Starting facts

- CAD-MAX starting HEAD：`38ee37c92efede18a18e9c81e94a38aa07895d12`。
- Branch：`dev`；开始时 `HEAD == origin/dev`，CAD-MAX worktree clean。
- Initial authority：schema 1；`PHASE_0 / COMPLETED`；next action
  `PLAN_PHASE_1_AUTOCAD_CONNECTION`。
- NQ reference HEAD：`7a023c627ff1c63d179abb1740016aae60e95125`。
- NQ 起始 worktree 有用户既有修改；用户明确允许本任务继续只读治理文档。本任务不读取该 diff、不修改、
  stash、reset、clean、commit 或 push NQ。

## Research source

- 用户提供并接受的 AutoCAD 2025/2026 MCP 研究基线；
- CAD-MAX `docs/research/REFERENCE_PROJECTS.md`；
- Autodesk 2025/2026 Developer and ObjectARX Help 官方入口；
- 8 个参考 GitHub 仓库的 HEAD 与 license metadata/LICENSE 路径核验。

仓库与本轮附件目录未发现 `deep-research-report_0717.md`，因此未读取或复制该报告。

## Files inspected

- CAD-MAX：`AGENTS.md`、root/current README、current STATUS/ROADMAP/GOVERNANCE/FACT_SOURCE/
  TESTING/WORKLOG、ARCHITECTURE、SECURITY、ROADMAP、REFERENCE_PROJECTS、ADR 0001、SDK setup、
  `scripts/docs/**`、`scripts/verify.ps1`、PR template、CI workflow。
- NQ：`AGENTS.md`、`docs/current/README.md`、`docs/current/GOVERNANCE_WORKFLOW.md`、
  `docs/current/FACT_SOURCE_INDEX.md`、`docs/current/CODEX_PROJECT_INSTRUCTIONS.md`、
  `docs/current/NQ_DH_CODEX_TASK_TEMPLATES.md`、`docs/current/NQ_DH_WORKFLOW_ROUTER_SKILL.md`、
  `scripts/docs/governance-workflow-contract.json`、`scripts/docs/check-current-authority.ps1`；全部只读。

## Rules adopted

- `STATUS.md` 单一 current authority，ROADMAP 只定义下一动作；
- Phase + numbered work batch；普通/高风险 lifecycle；
- `COMMITTED|CI_FAILED|FIX_REQUIRED` 与 `COMMITTED|CI_GREEN|CONTINUE_REQUIRED`；
- code-first、review-only no-diff、docs budget、append-only ledger；
- immutable two-digit attempt evidence；
- contract 驱动 checker、fail-closed hard blockers、exact-head CI；
- current docs 中文为主，enum/schema/path 保留英文。

## Rules explicitly rejected

未采用 NQ Gate/Freeze/tag、GateW、LIVE/PAPER trading、DH、exchange、credential/provider、release tag、
NQ module、archive role/manifest、NQ current status 或 NQ historical evidence。未复制 NQ 完整 contract，
只改写 CAD-MAX Phase/work-batch 所需最小语义。

## Architecture decisions

- AutoCAD Managed .NET API + embedded C# plugin；
- Python MCP + `127.0.0.1` HTTP/JSON + token；
- document-context request queue；
- Phase 1 只允许 `READ / PLAN / PREVIEW`；
- Handle + ObjectId + revision + fingerprint + selection snapshot；
- GitHub-hosted CI 保持 SDK-free，真实 AutoCAD 使用后续受控 Windows environment。

## Security impact

无安全能力放宽。AutoCAD/DWG/write/script 均保持未连接或未实现；loopback-only 保持不变；不提交
Autodesk binary、SDK、凭证、`.env` 或本机绝对路径。

## Validation

- `git diff --check`：`PASS`。
- Local docs governance：`PASS`；schema 1/schema 2 正例、要求的 fail-closed 负例、59 links、0 warning、
  0 error。
- Full `scripts/verify.ps1`：`PASS`；Ruff、mypy、16 Python tests、doctor、governance、locked .NET
  restore/build（0 warning / 0 error）、Contracts 1 test、Bridge/Core/Host 8 tests 全部成功。
- Independent review：`REVIEW_ACCEPTED|READY_TO_COMMIT`；P0 无，P1 无。
- Candidate scope review：18 个 task files；runtime/contracts/current authority/current ledger/CI workflow
  变更为 0。Closeout 只修改已授权的 authority、ROADMAP、ledger、evidence 与 root README。
- Autodesk binary/secret/path scan：`PASS`；Autodesk binary 0、敏感文件名 0、绝对个人路径 0、
  high-confidence secret value 0。
- Schema 2 closeout worktree full verify：`PASS`；authority regression、60 links、Python/.NET 全部成功。
- Real AutoCAD / DWG integration：`NOT_RUN`（本任务明确排除）。

## Candidate

- Candidate commit：`f67eed18aa2658ab05c8aa4d8bd0ed9b8fcbe120`。
- Candidate CI run：`29592363444`，`completed / success`，head SHA 与 candidate 精确一致。
- Candidate jobs：Governance `success`、Python 3.12 `success`、.NET 8 `success`。

## Closeout

- Closeout commit：`UNCOMMITTED`。
- Closeout CI run：`NOT_RUN`。
- Closeout jobs：`NOT_RUN`。
- Final authority：schema 2 closeout worktree 已准备；在 closeout commit 与其 exact-head CI 成功前不得报告
  最终 `CI_GREEN`。

## Known limitations

- 未运行 AutoCAD 2025/2026、未加载 Autodesk DLL、未读取或修改 DWG；
- self-hosted Windows integration runner 与 Golden DWG 尚未实现；
- closeout commit/run 无法在其自身 immutable Git tree 内自引用；实际 closeout SHA/run 必须在后续可追踪
  evidence attestation 或最终报告中记录，且该 attestation 自身仍需 exact-head CI。

## Rollback

- 未提交阶段：撤销本 attempt 列出的 CAD-MAX task files；
- 已推送阶段：分别 `git revert <closeout-sha>` 与 `git revert <candidate-sha>`，按逆序执行；
- 不 reset shared history，不 force push，不修改 NQ。
