# 当前状态

<!-- cad-max-current-authority:start
authority_schema=1
current_phase=PHASE_0
phase_status=IN_PROGRESS
next_phase=PHASE_1
next_action=COMPLETE_BOOTSTRAP_VALIDATION
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

- Phase 0：`IN PROGRESS`（进行中）。工程骨架已建立，当前仍需完成修复后的统一本地验证、transport smoke、提交/push 与 exact-head CI 绿色验收。
- Phase 1：`NOT STARTED`（未开始）。不得把插件边界或 Bridge Host 写成真实 AutoCAD 已连接。

## 唯一下一动作

`COMPLETE_BOOTSTRAP_VALIDATION`：完成 Phase 0 全量本地验证、stdio/Streamable HTTP smoke、提交与 `origin/dev` exact-head GitHub Actions 绿色验收。

## 固定安全事实

- AutoCAD runtime：`NOT CONNECTED`（未连接）。
- DWG read：`NOT IMPLEMENTED`（未实现）。
- DWG write：`NOT IMPLEMENTED`（未实现）。
- read-only：`ENABLED`（开启）。
- allow write：`DISABLED`（关闭）。
- allow script：`DISABLED`（关闭）。
- HTTP binding：`LOOPBACK ONLY`（仅回环地址）。

CI 绿色只证明仓库检查通过，不代表 AutoCAD、DWG 读取或 DWG 修改能力已经实现。
