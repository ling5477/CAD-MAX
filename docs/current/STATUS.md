# 当前状态

<!-- cad-max-current-authority:start
authority_schema=2
current_phase=PHASE_1
phase_status=IN_PROGRESS|NOT_FROZEN
accepted_work_batch=PHASE_1_4_READONLY_DOCUMENT_INSPECTION
accepted_work_batch_status=ACCEPTED|CI_GREEN
accepted_work_batch_commit=e62fded92df1064ffbd82b75cc910a568eb9fc7b
accepted_work_batch_ci_run=30741291154
work_batch=PHASE_1_MAINTENANCE_MCP_2026_07_28_STATELESS_MIGRATION
work_batch_status=NOT_STARTED
work_batch_commit=NONE
work_batch_ci_run=NOT_RUN
next_action=IMPLEMENT_PHASE_1_MAINTENANCE_MCP_2026_07_28_STATELESS_MIGRATION
autocad_runtime=CONNECTED
dwg_read=IMPLEMENTED
dwg_write=NOT_IMPLEMENTED
read_only=ENABLED
allow_write=DISABLED
allow_script=DISABLED
http_binding=LOOPBACK_ONLY
cad-max-current-authority:end -->

`docs/current/STATUS.md` 是 CAD-MAX 当前 Phase、accepted/work batch、下一动作和安全能力状态的唯一
authority。其他文档只能解释或链接本文件，不得建立独立状态。

## 当前 Phase 与 work batch

- Phase 0：`COMPLETED`（已完成）；历史 implementation candidate 为
  `4bec3fa042e27c105e636504bae16c2f00ebd1e7`，CI run `29578296421`。
- Phase 1：`IN PROGRESS / NOT FROZEN`（进行中 / 未冻结）。
- PHASE_1_PLAN：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）；candidate
  `f67eed18aa2658ab05c8aa4d8bd0ed9b8fcbe120` 的 exact-head CI run `29592363444`
  中 Governance、Python 3.12、.NET 8 全部成功。
- PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）；最终 candidate
  `d149162938b239948048662c9db342c4a0b1fce3` 的 exact-head CI run `29641896446` 中 Governance、
  Python 3.12、.NET 8 全部成功；AutoCAD 2025 已取得真实 plugin load、status command、Initialize
  与 Terminate evidence，AutoCAD 2026 因未安装保持 `NOT_RUN`。
- PHASE_1_MAINTENANCE_DEPENDENCIES：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）；最终 implementation
  candidate `ba69004657adaef217ed12dd99adbb07ec1e2be9` 的 exact-head CI run `29647121269` 中
  Governance、Python 3.12、.NET 8 全部成功；该插入式维护批次不代表 CAD 能力推进。
- PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）；最终 candidate
  `031ea139e73ffeacb4a5b717a8051afc9b7d287c` 的 exact-head CI run `29766557136` 中 Governance、
  Python 3.12、.NET 8 全部成功。认证 loopback Bridge 已在真实 AutoCAD 2025/2026 上完成 endpoints、
  端口冲突、shutdown、restart/reconnect 与 doctor 验收；focused security scan 的原 P3 已关闭。
- PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）；最终 candidate
  `8dc5d25630c681b1070b06443a80ca43a269c7bd` 的 exact-head CI run `29934893298` 中 Governance、
  Python 3.12、.NET 8 全部成功；AutoCAD 2025/2026 真实 main-thread/document-context 矩阵均通过。
- PHASE_1_4_READONLY_DOCUMENT_INSPECTION：`ACCEPTED / CI GREEN`（已接受 / CI 已通过）；最终 candidate
  `e62fded92df1064ffbd82b75cc910a568eb9fc7b` 的 exact-head CI run `30741291154` 中 Governance、
  Python 3.12、.NET 8 全部成功；AutoCAD 2025/2026 真实只读 document inspection 矩阵均通过，
  DBMOD 与 DWG SHA-256 均未变化。
- PHASE_1_MAINTENANCE_MCP_2026_07_28_STATELESS_MIGRATION：`NOT_STARTED`（未开始）；这是插入在
  Phase 1.4 与 Phase 1.5 之间的高风险协议维护 batch，未产生 implementation commit，未运行该 batch 的 CI。
- PHASE_1_5_READONLY_OBJECT_INSPECTION：等待 MCP maintenance 接受后恢复为唯一下一 work batch；当前未开始，
  未产生 implementation commit，未运行该 batch 的 CI。

## 唯一下一动作

`IMPLEMENT_PHASE_1_MAINTENANCE_MCP_2026_07_28_STATELESS_MIGRATION`：只允许将 Python MCP SDK 升级到
稳定 v2，将对外 MCP server 迁移到 `2026-07-28` 无状态协议，并以显式 instance/document handle 消除
隐藏 MCP session 对业务正确性的依赖；必须兼容 SDK v2 官方支持的 legacy revision，保持两个现有工具、
认证 loopback、read-only 与 Phase 1.4 document inspection 语义。不允许实现 Phase 1.5 object inspection、
DWG 修改或 write/script。

## 固定安全事实

- AutoCAD runtime：`CONNECTED`（已连接；仅限认证 loopback process-level endpoints）。
- DWG read：`IMPLEMENTED`（已实现；仅限已接受的 Phase 1.4 document metadata inspection）。
- DWG write：`NOT IMPLEMENTED`（未实现）。
- read-only：`ENABLED`（开启）。
- allow write：`DISABLED`（关闭）。
- allow script：`DISABLED`（关闭）。
- HTTP binding：`LOOPBACK ONLY`（仅回环地址）。

`CONNECTED` 与 `dwg_read=IMPLEMENTED` 只表示认证 loopback、bounded document context 与 Phase 1.4
allowlisted document metadata inspection 已接受；不表示 entity/layer/block 等对象读取或任何 DWG 修改已实现。
DWG write 与 write/script 能力仍保持关闭。
