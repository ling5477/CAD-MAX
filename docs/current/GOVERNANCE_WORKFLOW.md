# CAD-MAX Governance Workflow

本文定义 CAD-MAX 的人类可读执行流程。当前状态只由 [STATUS.md](STATUS.md) 的机器可读区块决定；
状态、transition、evidence 命名与 hard blocker 的机器事实由
`scripts/docs/governance-workflow-contract.json` 定义。checker 必须读取该 contract，不访问 GitHub，
也不把计划能力升级为已实现能力。

## 1. 事实源职责

- `STATUS.md`：唯一 current authority。
- current `ROADMAP.md`：唯一下一允许动作，不覆盖 STATUS。
- `FACT_SOURCE_INDEX.md`：文档职责与优先级。
- `TESTING.md` / `WORKLOG.md`：append-only evidence，不决定 Phase。
- `PHASE_1_AUTOCAD_CONNECTION_PLAN.md`：Phase 1 active plan 与 numbered work-batch 顺序，不决定 Phase。
- `docs/current/evidence/phase-1/**`：不可覆盖 task attempt，不决定 Phase。
- `docs/ARCHITECTURE.md`、`SECURITY.md`、`ROADMAP.md`：能力事实与设计。

如果机器区块缺失/重复、schema/transition/next action 非法、evidence 命名错误，或安全事实冲突，
`check-current-authority.ps1` 必须 fail closed。

## 2. 普通任务

```text
NOT_STARTED
→ IMPLEMENTED|SELF_REVIEWED
→ COMMITTED|CI_PENDING
→ ACCEPTED|CI_GREEN
```

普通任务不强制独立 review，但必须最小、可验证、可回滚。机械提交/push 不创建空洞文档。

## 3. 高风险任务

以下事项必须在实现后、提交前独立复核风险与回滚：

- `.github/workflows/**`、发布或部署；
- protocol/schema compatibility；
- credential、网络暴露、路径权限或日志脱敏；
- Autodesk SDK 引用与插件加载；
- `.bundle` / `PackageContents.xml`、thread/document context、installer/update；
- DWG write、script、transaction、Undo、幂等；
- P0/P1 安全或数据完整性修复。

```text
NOT_STARTED
→ IMPLEMENTED|PENDING_REVIEW
→ REVIEW_ACCEPTED|READY_TO_COMMIT
→ COMMITTED|CI_PENDING
→ ACCEPTED|CI_GREEN
```

CI 不能替代安全 review 或真实 AutoCAD 验证。

## 4. CI 状态

- `COMMITTED|CI_PENDING`：run 未结束，不得写为 green。
- `COMMITTED|CI_FAILED|FIX_REQUIRED`：exact-head CI 已完成且失败；保留 concrete commit/run，读取失败
  job/log，本地复现，做最小修复、review、全量验证与新提交后回到 `COMMITTED|CI_PENDING`。
- `COMMITTED|CI_GREEN|CONTINUE_REQUIRED`：当前技术子切片 exact-head CI 已成功，但同一 numbered
  work batch 仍有已授权工作；accepted work batch 保持不变，不初始化下一 batch。
- `ACCEPTED|CI_GREEN`：整个 numbered work batch 已完成；只用于 `HEAD == origin/dev` 且该 SHA 的
  所有 required GitHub Actions 成功。
- 失败修复不得删除检查、使用 `continue-on-error`、降低 analyzer/test 或仅重跑。
- 禁止 `COMMITTED|CI_FAILED|FIX_REQUIRED → ACCEPTED|CI_GREEN`；transition regression 必须 fail closed。

Work-batch/Phase closeout 使用两步：先让 implementation candidate exact-head CI 绿色；再更新
`STATUS.md`、current ROADMAP 与 append-only evidence 并推送 closeout commit。candidate run 不能替代
closeout run；最终仍以 closeout 后 exact HEAD 的 required CI 为准。

## 5. Docs Budget

- 普通代码变更默认不改 docs；确需记录时只追加一条 `WORKLOG`。
- 测试基线变化可追加 `TESTING` 与 `WORKLOG`。
- 当前 Phase 或下一动作变化才修改 `STATUS` / current `ROADMAP`。
- root README 只在入口、架构、启动命令或阶段总状态变化时修改。
- review-only 默认 no-diff；用户明确要求、合同冻结、安全/CI 计划或 Phase closeout 例外。
- 不为每个中间状态同步多份文档；最终 closeout 再更新 authority 与验证账本。
- 高风险/authority/真实 AutoCAD integration 使用既有 attempt evidence；机械 commit/push 不创建空 evidence。

## 6. Checker

```powershell
.\scripts\docs\verify-docs.ps1
```

该入口依次运行：

1. contract 驱动 authority checker 的 schema 1/schema 2 正例和 fail-closed 负例；
2. 当前真实 authority 校验；
3. Markdown 相对链接校验。

负例至少覆盖 authority block 缺失/重复、非法 schema、write/script/DWG/runtime 安全回归、accepted
commit/run 非法、failed → accepted、work-batch/next-action 错配、非法 attempt 名称和 ROADMAP 冲突。
checker 只读项目事实；回归 fixture 仅写入 Git 忽略的 `.tools/governance-tests/` 并在本次运行后清理。

## 7. Git 与回滚

- 日常集成分支为 `dev`；`main` 仅用于稳定发布。
- 提交使用 Conventional Commits，并按 fix/governance/closeout 拆分。
- 禁止 force push、reset shared history、跳过 hooks/checks 或提交生成产物。
- 已推送变更使用 `git revert <sha>` 回滚，再验证 revert commit 的 exact-head CI。

## 8. Phase + numbered work batch

Phase 1 顺序由 machine contract 与
[Phase 1 主计划](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)共同定义：

```text
PHASE_1_PLAN
→ PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP
→ PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE
→ PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH
→ PHASE_1_4_READONLY_DOCUMENT_INSPECTION
→ PHASE_1_5_READONLY_OBJECT_INSPECTION
→ PHASE_1_6_SELECTION_AND_ACCURACY
→ PHASE_1_7_PREVIEW_EVIDENCE_GOLDEN_DWG
→ PHASE_1_CLOSEOUT
```

每个 batch 只有一个主要目标、1 至 2 个工作日、独立回滚/测试/review/exact-head CI。当前 work batch
未完整接受前不得推进 `accepted_work_batch` 或启动下一 batch。

## 9. Task evidence

```text
docs/current/evidence/phase-1/README.md
docs/current/evidence/phase-1/<TASK-ID>.attempt-<NN>.md
```

- attempt 使用两位数字，不覆盖；失败/`BLOCKED` attempt 保留，重跑新增 attempt；
- 不创建空 evidence；普通低风险任务默认不要求独立 evidence；
- 高风险、authority、Phase closeout 与真实 AutoCAD integration 必须有 evidence；
- evidence 保存 task facts，不决定 current Phase；
- immutable Git commit 不能自引用自身 SHA/run；candidate/closeout 的已知事实记录在后续可追踪 attestation
  或最终报告，attestation 自身仍必须通过 exact-head CI，不能假写未来 run。

## 10. Hard blockers

Machine contract 至少定义并 fail closed：

- `CURRENT_AUTHORITY_CONFLICT`、`DIRTY_WORKTREE_SCOPE_UNKNOWN`、`BRANCH_MISMATCH`；
- `SDK_REFERENCE_UNSAFE`、`AUTODESK_BINARY_DETECTED`；
- `LOOPBACK_POLICY_VIOLATION`、`SECURITY_DEFAULT_REGRESSION`、`CONTRACT_MISMATCH`；
- `REVIEW_REQUIRED`、`CI_EXACT_HEAD_MISMATCH`、`CI_FAILED`；
- `EVIDENCE_INVALID`、`NEXT_ACTION_MISMATCH`。

Hard blocker 不得降为 warning。治理问题优先修 machine contract/checker regression，不在多个脚本复制
状态列表或按单个 blocker 添加特殊分支。

## 11. 禁止 churn

- 不为普通任务强制独立 review；
- 不为机械 commit/push 创建 evidence；
- 不为每个状态新建计划；
- 不重复维护 current 状态表；
- 不把 plan/review/first-run/rerun/closeout 拆成大量重复文档；
- 不为“保持文档一致”制造没有代码、测试、CI、安全或用户授权触发的 docs-only task。

## 12. Phase 0 完成门槛（历史基线）

- Python install/lint/format/type/tests/doctor 全绿。
- .NET locked restore/build/tests 全绿。
- docs governance 全绿。
- stdio 与 Streamable HTTP 均完成受控 smoke。
- 安全默认值与 capability honesty 保持不变。
- 工作区 clean，`HEAD == origin/dev`，exact-head CI 全绿。

任何一项缺失都只能报告 `IN_PROGRESS` 或明确 `BLOCKED`，不能报告 Phase 0 completed。
