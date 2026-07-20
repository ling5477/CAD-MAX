# Phase 1 task evidence

本目录保存 Phase 1 高风险任务、authority 变化、真实 AutoCAD integration 与 Phase closeout 的不可覆盖
attempt evidence。它不决定 current Phase；唯一 authority 仍是 [../../STATUS.md](../../STATUS.md)。

## 规则

- 文件名为 `<TASK-ID>.attempt-<NN>.md`，attempt 使用两位数字；
- 重跑新增 attempt，不覆盖旧文件；`BLOCKED` attempt 必须保留；
- 不创建空 evidence，不为普通低风险任务或机械 commit/push 强制建文件；
- evidence 中 `NOT_RUN`、`UNCOMMITTED`、失败与未验证事实不得改写为通过；
- Phase closeout 时只同步已经发生且可核验的 SHA/run，不用 candidate run 冒充 closeout run。

## Attempts

- [CADMAX-PHASE1-PLAN-AND-GOVERNANCE-SYNC.attempt-01.md](CADMAX-PHASE1-PLAN-AND-GOVERNANCE-SYNC.attempt-01.md)
- [CADMAX-PHASE1-1-AUTOCAD-PLUGIN-BOOTSTRAP.attempt-01.md](CADMAX-PHASE1-1-AUTOCAD-PLUGIN-BOOTSTRAP.attempt-01.md)
- [CADMAX-DEPENDABOT-PR-CLEANUP-AND-DEPENDENCY-MAINTENANCE.attempt-01.md](CADMAX-DEPENDABOT-PR-CLEANUP-AND-DEPENDENCY-MAINTENANCE.attempt-01.md)
- [CADMAX-PHASE1-2-LOOPBACK-BRIDGE-LIFECYCLE.attempt-01.md](CADMAX-PHASE1-2-LOOPBACK-BRIDGE-LIFECYCLE.attempt-01.md)
