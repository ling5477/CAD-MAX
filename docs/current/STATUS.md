# 当前状态

<!-- cad-max-current-authority:start
authority_schema=2
current_phase=PHASE_1
phase_status=IN_PROGRESS|NOT_FROZEN
accepted_work_batch=PHASE_1_PLAN
accepted_work_batch_status=ACCEPTED|CI_GREEN
accepted_work_batch_commit=f67eed18aa2658ab05c8aa4d8bd0ed9b8fcbe120
accepted_work_batch_ci_run=29592363444
work_batch=PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP
work_batch_status=NOT_STARTED
work_batch_commit=NONE
work_batch_ci_run=NOT_RUN
next_action=IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP
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
- PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP：`NOT_STARTED`（未开始）；未产生 implementation commit，
  未运行该 batch 的 CI。

## 唯一下一动作

`IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP`：只允许建立 AutoCAD 2025/2026 .NET 8 plugin
项目边界、本机 Autodesk DLL 安全引用、开发 `.bundle` / `PackageContents.xml` 与 load/unload 证据；
不允许图纸读取、DWG 修改、write/script tool 或下一 work batch 实现。执行细节见
[Phase 1 主计划](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 固定安全事实

- AutoCAD runtime：`NOT CONNECTED`（未连接）。
- DWG read：`NOT IMPLEMENTED`（未实现）。
- DWG write：`NOT IMPLEMENTED`（未实现）。
- read-only：`ENABLED`（开启）。
- allow write：`DISABLED`（关闭）。
- allow script：`DISABLED`（关闭）。
- HTTP binding：`LOOPBACK ONLY`（仅回环地址）。

CI 绿色只接受 Phase 1 plan/governance candidate，不代表 AutoCAD、Autodesk SDK、plugin load、DWG
读取或 DWG 修改已实现。
