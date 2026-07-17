# ADR 0002：AutoCAD 2025/2026 MCP runtime baseline

状态：Accepted
日期：2026-07-17

## Context

Phase 1 需要在不放宽 CAD-MAX 安全默认值的前提下，建立 AutoCAD 2025/2026 的真实本机连接、
document-context dispatch 和可审计只读能力。该边界必须同时满足 MCP transport 的可测试性、
AutoCAD Managed .NET API 的进程内线程约束，以及 GitHub-hosted CI 不安装 AutoCAD 的现实。

## Decision

采用以下固定架构：

1. Python MCP server 负责 stdio/Streamable HTTP、schema、输入/权限校验、Bridge client 和客户端安全错误。
2. AutoCAD 2025/2026 进程内 C# 插件负责 Managed .NET API、主线程派发、application/document context、
   `DocumentLock`、Transaction、对象映射和后验读取证据。
3. Python 与插件使用 `127.0.0.1` HTTP/JSON；协议必须有 token、timeout、cancellation、
   `requestId`、`traceId`、`schemaVersion`、structured error、health 与 dynamic capabilities。
4. Phase 1 只启用 `READ`、`PLAN`、`PREVIEW`；`WRITE`、`DESTRUCTIVE`、`SCRIPT` 保持关闭。
5. Autodesk DLL 只允许由获许可的本机 AutoCAD 安装提供，通过未提交的 machine-local 配置引用；
   `Copy Local` 必须关闭，默认 CI solution graph 不依赖 Autodesk DLL。

## Rejected

- Python COM production backend：不作为生产主路径。
- Named Pipe first version：首版只维护一套 HTTP/JSON contract。
- gRPC first version：不引入额外 code generation/runtime。
- ObjectARX first version：Phase 1 不需要原生 ABI 范围。
- AutoLISP、AutoCAD command string、arbitrary script：执行面无法满足 allowlist 与后验验证。
- AutoCAD Core Console、APS AutoCAD Automation：不属于本机交互式 Phase 1。
- AutoCAD 2024：保持在独立 .NET Framework 4.8 兼容线，不混入 .NET 8 solution。

## Consequences

### 正向结果

- Autodesk 类型不进入 Python/JSON 公共 contract；
- 连接状态、capability 与错误可以在无 AutoCAD 的 CI 中进行 contract test；
- 所有未实现 operation 都能在注册层和 dispatcher 层 fail closed；
- document context、对象 revision/fingerprint 与证据边界有明确归属。

### 成本与风险

- 必须维护 Python/JSON/C# 双语言 contract 的 camelCase 语义一致性；
- 进程内插件缺陷可能影响 AutoCAD 稳定性，因此 handler、queue、资源释放必须最小化；
- 所有 AutoCAD API 访问必须经过 document-context request queue；
- SDK 引用依赖本机获许可安装，不能由公共 CI 验证；
- 需要受控 self-hosted Windows integration runner 或人工测试机验证真实 AutoCAD；
- 不得在持有 `DocumentLock` 或 Transaction 时等待外部 HTTP。

## Guardrails

- 默认 `readOnly=true`、`allowWrite=false`、`allowScript=false`、`httpHost=127.0.0.1`；
- 未实现能力不注册，unknown operation 返回稳定的 `NOT_IMPLEMENTED`；
- Preview 只是第二证据，数据库级后验读取才是对象事实；
- Phase 1 不读取或修改真实业务 DWG；integration 使用可丢弃 Golden DWG；
- CI 绿色不表示 AutoCAD runtime、DWG read 或 DWG write 已实现。

## Related

- [研究接受基线](../research/AUTOCAD_MCP_RESEARCH_ACCEPTANCE.md)
- [Phase 1 主计划](../current/PHASE_1_AUTOCAD_CONNECTION_PLAN.md)
- [ADR 0001](0001-python-mcp-dotnet-autocad-bridge.md)
