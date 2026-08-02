# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_MAINTENANCE_MCP_2026_07_28_STATELESS_MIGRATION`

只允许完成以下单一 work batch：

1. 将 Python MCP SDK 从 v1 升级到稳定 v2，并保留项目自有 `httpx` Bridge client；
2. 使用官方 `MCPServer` 同时支持 `2026-07-28` 无状态请求与 SDK v2 官方 legacy fallback；
3. 从 tool input 移除 `request_id` / `trace_id`，改由 SDK request context 安全映射或生成；
4. 要求 drawing document-level 请求显式携带 instance/document handle，backend cache 只作性能优化；
5. 保持 Streamable HTTP token auth、loopback-only、stdio、structured output 与两个现有工具；
6. 完成协议 conformance、多客户端隔离、安全审查、AutoCAD 2025/2026 只读 smoke 与 exact-head CI。

## 当前禁止

- 实现 entities、layers、blocks、attributes、styles、selection、preview 或任何 Phase 1.5+ capability；
- 修改 AutoCAD C# Plugin 或已接受的 Phase 1.4 DWG read 业务语义；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
