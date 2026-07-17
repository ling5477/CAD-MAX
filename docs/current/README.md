# Current Docs

`docs/current/` 是 CAD-MAX 的当前治理控制面。当前状态必须解析 [STATUS.md](STATUS.md) 顶部唯一的 `cad-max-current-authority` 区块；本入口不复制独立阶段状态。

## Authority Map

| 职责 | 文件 | 是否决定当前 Phase |
| --- | --- | --- |
| 唯一状态与安全事实 | [STATUS.md](STATUS.md) | 是 |
| 下一允许动作 | [ROADMAP.md](ROADMAP.md) | 否；服从 STATUS |
| 事实源职责 | [FACT_SOURCE_INDEX.md](FACT_SOURCE_INDEX.md) | 否 |
| 执行与 CI 流程 | [GOVERNANCE_WORKFLOW.md](GOVERNANCE_WORKFLOW.md) | 否 |
| Phase 1 active plan | [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md) | 否；定义 numbered work batch |
| Agent 执行指引 | [CODEX_PROJECT_INSTRUCTIONS.md](CODEX_PROJECT_INSTRUCTIONS.md) / [CAD_MAX_CODEX_TASK_TEMPLATES.md](CAD_MAX_CODEX_TASK_TEMPLATES.md) | 否 |
| Phase 1 attempt evidence | [evidence/phase-1/README.md](evidence/phase-1/README.md) | 否；不可覆盖 evidence |
| 验证证据 | [TESTING.md](TESTING.md) | 否；append-only |
| 工作证据 | [WORKLOG.md](WORKLOG.md) | 否；append-only |

## Current Is Not

- 不是 AutoCAD runtime 已连接。
- 不是已经支持读取、修改、保存或导出 DWG。
- 不是允许 write、script、任意路径或非 loopback 网络访问。
- 不是用 CI 结果替代真实 AutoCAD 集成测试。

技术能力文档仍位于 `docs/`；它们描述能力和设计，不推进当前 Phase。

Machine contract 位于 `scripts/docs/governance-workflow-contract.json`。authority checker 只读该 contract 与
本地 current/evidence 文件，不访问 GitHub、网络或 AutoCAD。
