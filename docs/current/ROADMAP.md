# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整能力路线见 [../ROADMAP.md](../ROADMAP.md)。

## 当前允许动作

`PLAN_PHASE_1_AUTOCAD_CONNECTION`：

Phase 0 implementation candidate `4bec3fa042e27c105e636504bae16c2f00ebd1e7` 的 exact-head CI run `29578296421` 已完成且三个 jobs 全部成功。当前只允许形成 Phase 1 可审查计划；本 closeout 文档提交仍必须取得自身 exact-head CI 绿色后才能完成交付。

Phase 1 计划必须覆盖：

1. AutoCAD 2025/2026 与 .NET 8 兼容边界。
2. Autodesk DLL 仅本机、未提交引用策略。
3. 插件启动/停止、健康状态和 localhost 注册。
4. application/document context queue、锁、取消、超时和资源释放。
5. 无 AutoCAD、无活动文档、SDK 缺失和版本不匹配的 fail-closed 行为。
6. 仍不注册 write/script 工具，不开启通用 DWG 修改。

## 当前禁止

- 真实 DWG 读取或修改。
- AutoCAD 2024 / .NET Framework 4.8 混入当前 .NET 8 solution。
- 提交 Autodesk SDK/DLL 或本机引用路径。
- 默认监听 `0.0.0.0`、局域网或公网。
- 注册 write、script 或其他未真实实现的 MCP 工具。
