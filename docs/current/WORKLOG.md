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

## 2026-07-17 / PHASE-0-CLOSEOUT

- 实现候选 `4bec3fa042e27c105e636504bae16c2f00ebd1e7` 已 push 到 `origin/dev`。
- exact-head CI run `29578296421` 为 `completed / success`，Governance、Python 3.12、.NET 8 全绿。
- Phase 0 authority 更新为 `COMPLETED`，下一动作更新为 `PLAN_PHASE_1_AUTOCAD_CONNECTION`；Phase 1 仍为 `NOT STARTED`。
- 本条是 docs-only closeout；最终交付仍要求 closeout SHA 的 exact-head CI 绿色。

## 2026-07-17 / CADMAX-PHASE1-PLAN-AND-GOVERNANCE-SYNC

- `PHASE_1_PLAN` 的 research acceptance、ADR、Phase 1 active plan、numbered work batches、governance
  contract、checker regression、Codex instructions/templates 与 immutable attempt evidence 已完成。
- Candidate `f67eed18aa2658ab05c8aa4d8bd0ed9b8fcbe120` 已 push；exact-head CI run
  `29592363444` 中 Governance、Python 3.12、.NET 8 全绿。
- Schema 2 closeout 将 `PHASE_1_PLAN` 记录为 `ACCEPTED|CI_GREEN`，初始化
  `PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP / NOT_STARTED`，唯一下一动作是
  `IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP`。
- 安全事实保持：AutoCAD `NOT_CONNECTED`、DWG read/write `NOT_IMPLEMENTED`、write/script
  `DISABLED`、HTTP `LOOPBACK_ONLY`；未运行真实 AutoCAD 或 DWG integration。
- 本条写入时 closeout 尚未 commit/push，最终接受仍取决于 closeout SHA 的 exact-head CI。

## 2026-07-17 / CADMAX-PHASE1-PLAN-AND-GOVERNANCE-SYNC authority closeout

- Authority closeout `92aaa83376eebeb47b9f02fae269cd2d02822105` 已 push 到 `origin/dev`。
- Closeout exact-head CI run `29592807681` 为 `completed / success`；Governance、Python 3.12、
  .NET 8 全绿。
- Schema 2 authority 已验证；唯一下一动作保持
  `IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP`，该 batch 仍为 `NOT_STARTED`。
- 本条只追记已发生的 closeout Git/CI 事实；不启动 plugin bootstrap，不改变 AutoCAD/DWG 安全状态。

## 2026-07-18 / CADMAX-PHASE1-1-AUTOCAD-PLUGIN-BOOTSTRAP closeout

- SDK-free plugin core、独立 SDK-bound adapter、开发 `.bundle`、安全安装/卸载脚本、生命周期 evidence
  与失败路径测试已完成；未实现 MCP-to-AutoCAD 通信、document/DWG 访问或 listener。
- AutoCAD 2025 已完成官方 bundle loader 真实加载、`CADMAXPLUGINSTATUS`、Initialize 与正常关闭后的
  Terminate 验收；AutoCAD 2026 未安装并保持 `NOT_RUN`。开发 bundle 已按固定 ProductCode 精确卸载。
- Implementation commit `dfb11cb9acfed60cc4ef246f55018240ac5e8cb4` 的首次 run `29640778847`
  暴露 PowerShell 成功退出码归一化缺陷；最小修复后的最终 candidate
  `d149162938b239948048662c9db342c4a0b1fce3` 在 run `29641896446` 的 Governance、Python 3.12、
  .NET 8 全绿。
- 独立复核结论为 `P0=0 / P1=0 / REVIEW_ACCEPTED`；原完整 diff 与 focused CI 修复扫描均无可报告
  finding。统一本地验证通过，固定安全事实未变化。
- 本次 docs-only closeout 接受 `PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP / ACCEPTED|CI_GREEN`，初始化
  `PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE / NOT_STARTED`；唯一下一动作变更为
  `IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE`。
- Closeout commit SHA 与 exact-head CI 在本文成文时尚不存在；完成后只在最终报告记录，不创建第三个
  纯 attestation commit。AutoCAD runtime 继续为 `NOT_CONNECTED`，DWG read/write 继续为
  `NOT_IMPLEMENTED`，write/script 继续为 `DISABLED`。
