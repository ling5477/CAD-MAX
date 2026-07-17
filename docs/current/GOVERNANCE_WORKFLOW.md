# CAD-MAX Governance Workflow

本文定义 CAD-MAX 的人类可读执行流程。当前状态只由 [STATUS.md](STATUS.md) 的机器可读区块决定；checker 不访问 GitHub，也不把计划能力升级为已实现能力。

## 1. 事实源职责

- `STATUS.md`：唯一 current authority。
- current `ROADMAP.md`：唯一下一允许动作，不覆盖 STATUS。
- `FACT_SOURCE_INDEX.md`：文档职责与优先级。
- `TESTING.md` / `WORKLOG.md`：append-only evidence，不决定 Phase。
- `docs/ARCHITECTURE.md`、`SECURITY.md`、`ROADMAP.md`：能力事实与设计。

如果机器区块缺失、重复、字段非法，或安全事实与 Phase 0 边界冲突，`check-current-authority.ps1` 必须 fail closed。

## 2. 普通任务

```text
PRECHECK
→ IMPLEMENTED
→ LOCALLY_VERIFIED
→ COMMITTED
→ PUSHED
→ EXACT_HEAD_CI_GREEN
```

普通任务不强制独立 review，但必须最小、可验证、可回滚。机械提交/push 不创建空洞文档。

## 3. 高风险任务

以下事项必须在实现后、提交前独立复核风险与回滚：

- `.github/workflows/**`、发布或部署；
- protocol/schema compatibility；
- credential、网络暴露、路径权限或日志脱敏；
- Autodesk SDK 引用与插件加载；
- DWG write、script、transaction、Undo、幂等；
- P0/P1 安全或数据完整性修复。

```text
PRECHECK
→ IMPLEMENTED / PENDING_REVIEW
→ REVIEW_ACCEPTED / READY_TO_COMMIT
→ COMMITTED / CI_PENDING
→ ACCEPTED / CI_GREEN
```

CI 不能替代安全 review 或真实 AutoCAD 验证。

## 4. CI 状态

- `CI_PENDING`：run 未结束，不得写为 green。
- `CI_FAILED / FIX_REQUIRED`：读取失败 job/log，本地复现，做最小修复，全量验证后新提交。
- `CI_GREEN`：只用于 `HEAD == origin/dev` 且该 SHA 的所有必需 GitHub Actions 成功。
- 失败修复不得删除检查、使用 `continue-on-error`、降低 analyzer/test 或仅重跑。

Phase closeout 使用两步：先让实现候选 exact-head CI 绿色；再更新 `STATUS.md` 为完成并推送 closeout commit，最终仍以 closeout SHA 的 exact-head CI 为准。

## 5. Docs Budget

- 普通代码变更默认不改 docs；确需记录时只追加一条 `WORKLOG`。
- 测试基线变化可追加 `TESTING` 与 `WORKLOG`。
- 当前 Phase 或下一动作变化才修改 `STATUS` / current `ROADMAP`。
- root README 只在入口、架构、启动命令或阶段总状态变化时修改。
- review-only 默认 no-diff；用户明确要求、合同冻结、安全/CI 计划或 Phase closeout 例外。
- 不为每个中间状态同步多份文档；最终 closeout 再更新 authority 与验证账本。

## 6. Checker

```powershell
.\scripts\docs\verify-docs.ps1
```

该入口依次运行：

1. authority checker 的正/负例回归；
2. 当前真实 authority 校验；
3. Markdown 相对链接校验。

checker 只读项目事实；回归 fixture 仅写入 Git 忽略的 `.tools/governance-tests/`。

## 7. Git 与回滚

- 日常集成分支为 `dev`；`main` 仅用于稳定发布。
- 提交使用 Conventional Commits，并按 fix/governance/closeout 拆分。
- 禁止 force push、reset shared history、跳过 hooks/checks 或提交生成产物。
- 已推送变更使用 `git revert <sha>` 回滚，再验证 revert commit 的 exact-head CI。

## 8. Phase 0 完成门槛

- Python install/lint/format/type/tests/doctor 全绿。
- .NET locked restore/build/tests 全绿。
- docs governance 全绿。
- stdio 与 Streamable HTTP 均完成受控 smoke。
- 安全默认值与 capability honesty 保持不变。
- 工作区 clean，`HEAD == origin/dev`，exact-head CI 全绿。

任何一项缺失都只能报告 `IN_PROGRESS` 或明确 `BLOCKED`，不能报告 Phase 0 completed。
