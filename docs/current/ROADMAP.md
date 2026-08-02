# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_5_READONLY_OBJECT_INSPECTION`

只允许开始以下单一 work batch：

1. 仅实现 Phase 1.5 定义的 bounded、read-only object inspection（entities、layers、blocks、attributes、
   styles、paging、stable sorting 与 DTO/schema），不扩展到 selection 或 preview；
2. 沿用已接受的 MCP v2 `2026-07-28` 无状态协议、legacy fallback、显式 instance/document handle、
   Streamable HTTP token auth、loopback-only、stdio、structured output 与两个现有工具；
3. 保持 Phase 1.4 的 document inspection 语义和所有固定安全默认值；
4. 为该 work batch 完成对应的协议、read-only、安全、AutoCAD 2025/2026 和 exact-head CI 验收。

## 当前禁止

- 实现 selection、preview 或任何 Phase 1.6+ capability；
- 修改已接受的 Phase 1.4 DWG read 业务语义；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
