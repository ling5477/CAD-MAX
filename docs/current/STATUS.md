# 当前状态

<!-- cad-max-current-authority:start
authority_schema=2
current_phase=PHASE_1
phase_status=IN_PROGRESS|NOT_FROZEN
accepted_work_batch=PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP
accepted_work_batch_status=ACCEPTED|CI_GREEN
accepted_work_batch_commit=d149162938b239948048662c9db342c4a0b1fce3
accepted_work_batch_ci_run=29641896446
work_batch=PHASE_1_MAINTENANCE_DEPENDENCIES
work_batch_status=COMMITTED|CI_PENDING
work_batch_commit=PENDING
work_batch_ci_run=PENDING
next_action=PHASE_1_MAINTENANCE_DEPENDENCIES_CI_WAIT_OR_INVESTIGATION
autocad_runtime=NOT_CONNECTED
dwg_read=NOT_IMPLEMENTED
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
- PHASE_1_MAINTENANCE_DEPENDENCIES：`COMMITTED / CI_PENDING`；Group A 候选在本提交成文时尚无可
  自引用的 commit SHA 或 CI run，因此字段使用 contract 允许的 `PENDING`，不得据此宣称 CI 绿色。
- PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE：仍为 `NOT_STARTED`（未开始）；依赖维护接受前不得启动。

## 唯一下一动作

`PHASE_1_MAINTENANCE_DEPENDENCIES_CI_WAIT_OR_INVESTIGATION`：提交并推送 Group A 候选后，只允许核对
该 exact HEAD 的 Governance、Python 3.12、.NET 8 jobs 与 Node runtime annotations；失败时进入最小
CI blocker 修复，不得启动 Phase 1.2 或宣称 maintenance accepted。

## 固定安全事实

- AutoCAD runtime：`NOT CONNECTED`（未连接）。
- DWG read：`NOT IMPLEMENTED`（未实现）。
- DWG write：`NOT IMPLEMENTED`（未实现）。
- read-only：`ENABLED`（开启）。
- allow write：`DISABLED`（关闭）。
- allow script：`DISABLED`（关闭）。
- HTTP binding：`LOOPBACK ONLY`（仅回环地址）。

Phase 1.1 的 AutoCAD 2025 真实加载证据只证明 plugin bootstrap 边界；尚未建立 Python MCP →
loopback Bridge → AutoCAD 连接，不代表 AutoCAD runtime 已连接，也不代表 DWG read/write 已实现。
