# Current Docs

`docs/current/` 是 CAD-MAX 的当前治理控制面。当前状态必须解析 [STATUS.md](STATUS.md) 顶部唯一的 `cad-max-current-authority` 区块；本入口不复制独立阶段状态。

## Authority Map

| 职责 | 文件 | 是否决定当前 Phase |
| --- | --- | --- |
| 唯一状态与安全事实 | [STATUS.md](STATUS.md) | 是 |
| 下一允许动作 | [ROADMAP.md](ROADMAP.md) | 否；服从 STATUS |
| 事实源职责 | [FACT_SOURCE_INDEX.md](FACT_SOURCE_INDEX.md) | 否 |
| 执行与 CI 流程 | [GOVERNANCE_WORKFLOW.md](GOVERNANCE_WORKFLOW.md) | 否 |
| 验证证据 | [TESTING.md](TESTING.md) | 否；append-only |
| 工作证据 | [WORKLOG.md](WORKLOG.md) | 否；append-only |

## Current Is Not

- 不是 AutoCAD runtime 已连接。
- 不是已经支持读取、修改、保存或导出 DWG。
- 不是允许 write、script、任意路径或非 loopback 网络访问。
- 不是用 CI 结果替代真实 AutoCAD 集成测试。

技术能力文档仍位于 `docs/`；它们描述能力和设计，不推进当前 Phase。
