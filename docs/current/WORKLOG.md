# 工作证据账本

本文件为 append-only work ledger，只记录已经发生的事实，不决定当前 Phase。

## 2026-07-17 / BOOTSTRAP-CI-FIX-AND-GOVERNANCE

- 状态：`IN PROGRESS`（进行中）。
- 范围：修复 bootstrap commit 的 .NET CI 编译错误；使 Windows 统一验证使用仓库内 pytest 临时目录；建立 current authority、docs budget、只读 checker、PR/CI 治理入口。
- 明确不涉及：真实 AutoCAD、DWG 读取/修改、Autodesk SDK/DLL、非 loopback 网络、write/script 工具。
- 收尾要求：全量本地验证与 transport smoke 通过，提交/push `dev`，latest exact-head CI 绿色后追加 closeout 证据。

## 2026-07-17 / BOOTSTRAP-CI-FIX-AND-GOVERNANCE local close

- 状态：`IMPLEMENTED / LOCALLY VERIFIED / UNCOMMITTED`（已实现 / 本地已验证 / 未提交）。
- .NET CI 编译问题、Windows pytest TEMP 权限和统一验证已修复；未关闭 analyzer、测试或 warnings-as-errors。
- NQ 治理的 authority、docs budget、ledger、checker、PR/CI 门禁已按 CAD-MAX Phase 0 精简迁移；未复制交易 Gate/archive 状态机。
- stdio 与 Streamable HTTP 已完成协议级 smoke；没有真实 AutoCAD 或 DWG 操作。
- 高风险提交前复核：workflow YAML、最小 permissions、timeout、authority 安全负例、文档链接、whitespace、tracked 文件卫生和 revert 回滚路径均通过检查。
- 下一动作仍为提交、push `dev` 并取得最新 exact-head CI 绿色；当前 Phase 保持 `IN_PROGRESS`。
