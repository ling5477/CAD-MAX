# 当前状态

<!-- cad-max-current-authority:start
authority_schema=1
current_phase=PHASE_0
phase_status=COMPLETED
next_phase=PHASE_1
next_action=PLAN_PHASE_1_AUTOCAD_CONNECTION
autocad_runtime=NOT_CONNECTED
dwg_read=NOT_IMPLEMENTED
dwg_write=NOT_IMPLEMENTED
read_only=ENABLED
allow_write=DISABLED
allow_script=DISABLED
http_binding=LOOPBACK_ONLY
cad-max-current-authority:end -->

`docs/current/STATUS.md` 是 CAD-MAX 当前 Phase、下一动作和安全能力状态的唯一 authority。其他文档只能解释或链接本文件，不得建立独立状态。

## 当前 Phase

- Phase 0：`COMPLETED`（已完成）。工程骨架、统一本地验证、stdio/Streamable HTTP smoke 与实现候选 exact-head CI 已通过；实现候选为 `4bec3fa042e27c105e636504bae16c2f00ebd1e7`，CI run 为 `29578296421`，Governance、Python 3.12、.NET 8 均成功。
- Phase 1：`NOT STARTED`（未开始）。不得把插件边界或 Bridge Host 写成真实 AutoCAD 已连接。

## 唯一下一动作

`PLAN_PHASE_1_AUTOCAD_CONNECTION`：只允许规划 AutoCAD 2025/2026 本机 SDK 引用、插件生命周期、document context queue、localhost 注册、取消/超时和失败模式；不授权真实 DWG write 或通用 CAD 操作。

## 固定安全事实

- AutoCAD runtime：`NOT CONNECTED`（未连接）。
- DWG read：`NOT IMPLEMENTED`（未实现）。
- DWG write：`NOT IMPLEMENTED`（未实现）。
- read-only：`ENABLED`（开启）。
- allow write：`DISABLED`（关闭）。
- allow script：`DISABLED`（关闭）。
- HTTP binding：`LOOPBACK ONLY`（仅回环地址）。

CI 绿色只证明仓库检查通过，不代表 AutoCAD、DWG 读取或 DWG 修改能力已经实现。
