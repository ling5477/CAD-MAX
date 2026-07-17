# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整能力路线见 [../ROADMAP.md](../ROADMAP.md)。

## 当前允许动作

`COMPLETE_BOOTSTRAP_VALIDATION`：

1. 完成 Python、.NET、documentation governance 的统一验证。
2. 对 stdio 与 Streamable HTTP 做受控启动/停止 smoke。
3. 确认未跟踪 Autodesk DLL、`.env`、凭证、缓存或构建产物。
4. 提交并推送 `dev`，确认 `HEAD == origin/dev`。
5. 等待 exact-head GitHub Actions 全部成功。
6. 通过独立 closeout 提交把 Phase 0 更新为 `COMPLETED`，并再次取得 exact-head CI 绿色。

## Phase 0 完成后

唯一允许的后续动作变为 `PLAN_PHASE_1_AUTOCAD_CONNECTION`。Phase 1 必须先冻结 AutoCAD 2025/2026 SDK 本机引用、插件生命周期、document context、localhost 注册和失败模式；不得直接开启通用 DWG 写入。

## 当前禁止

- 真实 DWG 读取或修改。
- AutoCAD 2024 / .NET Framework 4.8 混入当前 .NET 8 solution。
- 提交 Autodesk SDK/DLL 或本机引用路径。
- 默认监听 `0.0.0.0`、局域网或公网。
- 注册 write、script 或其他未真实实现的 MCP 工具。
