# 事实源索引

本索引定义文档职责，不复制独立阶段判定。发生冲突时先读取 [STATUS.md](STATUS.md)；无法消除时报告 `BLOCKED / CURRENT_AUTHORITY_CONFLICT`。

## Current Authority

1. [STATUS.md](STATUS.md)：唯一当前 Phase、下一动作与安全能力状态。
2. [ROADMAP.md](ROADMAP.md)：当前下一允许动作，不能覆盖 STATUS。
3. [README.md](README.md) 与仓库根 `README.md`：入口和短摘要。
4. [GOVERNANCE_WORKFLOW.md](GOVERNANCE_WORKFLOW.md)：执行、review、提交、CI 与回滚规则。

## Capability Facts

- [../ARCHITECTURE.md](../ARCHITECTURE.md)：Python MCP、C# Bridge、AutoCAD Plugin 的架构和信任边界。
- [../SECURITY.md](../SECURITY.md)：安全默认值、网络、路径、日志与未来 write gate。
- [../ROADMAP.md](../ROADMAP.md)：Phase 0-8 能力路线。
- [../DEVELOPMENT.md](../DEVELOPMENT.md)：本地工具链、启动和验证命令。
- [../../contracts/README.md](../../contracts/README.md)：语言无关 wire contract。

能力文档描述已实现或计划能力，但不能把计划写成当前实现，也不能推进 Phase。

## Evidence Ledgers

- [TESTING.md](TESTING.md)：append-only 验证证据；失败和重跑都保留。
- [WORKLOG.md](WORKLOG.md)：append-only 工作证据；不决定当前 Phase。

## Historical Evidence

当前尚未建立 Phase freeze archive。未来 archive 只能保存已经完成并审查的历史证据，且永远不能覆盖 current authority。
