# AutoCAD 2025/2026 MCP 研究接受基线

状态：`ACCEPTED FOR PHASE 1 PLANNING`（已接受用于 Phase 1 规划）
核验日期：2026-07-17
适用范围：CAD-MAX `PHASE_1_PLAN`

本文固化本轮已经接受的研究结论，作为
[Phase 1 主计划](../current/PHASE_1_AUTOCAD_CONNECTION_PLAN.md)与
[ADR 0002](../adr/0002-autocad-2025-2026-mcp-runtime-baseline.md)的输入。本文不代表
AutoCAD 已连接、DWG 已读取或任何写能力已实现。

## 1. 研究范围与证据口径

本轮只回答以下问题：

- AutoCAD 2025/2026 的进程内主开发入口与运行时边界；
- MCP、Python、C# 插件之间的职责与本机通信方式；
- Phase 1 的只读能力、对象准确性和验证边界；
- 参考项目可以借鉴的架构思想及许可证限制。

研究输入为：

1. 用户提供并明确接受的 AutoCAD 2025/2026 MCP 深度研究结论；
2. CAD-MAX 现有 [reference project review](REFERENCE_PROJECTS.md)；
3. Autodesk 2025/2026 官方 Developer and ObjectARX Help 入口；
4. 参考仓库在核验日的 GitHub HEAD、repository license metadata 与 LICENSE 路径。

在仓库及本轮附件目录内未发现 `deep-research-report_0717.md`，因此没有复制或引用该报告，
也没有把任何 Deep Research 内部 citation token 写入仓库。尚未通过真实 AutoCAD 验证的事实均在
“未验证项”中保留。

## 2. 已接受的官方入口

Phase 1 的 AutoCAD 主开发入口固定为：

- AutoCAD 2025 / AutoCAD 2026；
- Windows；
- .NET 8；
- AutoCAD Managed .NET API；
- 加载到 AutoCAD 进程内的 C# 插件。

真正访问 `Document`、`Database`、`Transaction`、`Editor`、`SelectionSet`、`Layer`、
`Block` 与 `Entity` 的代码必须位于进程内插件。Python 只负责 MCP transport、schema、
请求/权限校验、Bridge client、结构化响应和客户端安全错误，不得以 COM 绕过 C# Bridge 形成
生产主路径。

## 3. 已接受架构

```text
MCP Host
  → stdio / Streamable HTTP
Python MCP Server
  → 127.0.0.1 HTTP/JSON
AutoCAD C#/.NET Plugin
  → document-context request queue
AutoCAD Managed .NET API
  → DWG
```

内部通信第一版固定使用 loopback HTTP/JSON，并要求：

- `host=127.0.0.1`，不默认监听 `0.0.0.0`；
- 插件端 token、bounded timeout、cancellation；
- `requestId`、`traceId`、`schemaVersion`；
- structured error、health、capabilities 与插件生命周期状态；
- 所有未注册或未完成 operation 均 fail closed。

第一版不采用 Named Pipe、gRPC、WebSocket 或 Python COM 生产后端。这是范围选择，不表示这些
技术永久不可用；重新评估必须由未来 authority 明确授权。

## 4. Phase 1 接受范围

Phase 1 只允许 `READ`（读取）、`PLAN`（规划）与 `PREVIEW`（预览）等级：

- 插件装载/卸载、版本、心跳、健康、实例和断开/恢复状态；
- 当前文档、文档列表、单位、范围、布局和 revision；
- 图层、文字/标注/线型样式；
- 实体数量、按类型统计、分页实体概要；
- 块定义、块引用与块属性概要；
- 当前 `PickFirst` 选择集；
- Handle 解析、只读几何测量；
- PDF/PNG preview；
- dynamic capabilities、evidence 与不含敏感信息的 audit logging。

工具按 8 个顶层组规划：`cad_system`、`drawing`、`query`、`layer`、`block`、`style`、
`selection`、`preview`。每组内部使用受限 `operation` enum；不得扩张为 100 个以上顶层工具。

## 5. Phase 1 非范围

Phase 1 不允许任何 DWG mutation，包括 create、update、delete、move、rotate、scale、mirror、
offset、trim、extend、purge、recover 或保存修改。以下能力同样不进入 Phase 1：

- `WRITE`、`DESTRUCTIVE`、`SCRIPT` 执行等级；
- 任意 AutoLISP、AutoCAD command string 或 script；
- ObjectARX、AutoCAD Core Console、APS AutoCAD Automation、RealDWG、ODA；
- AutoCAD 2024 / .NET Framework 4.8；
- AutoCAD LT、中望 CAD、浩辰 CAD、天正、三维建模、云端多租户；
- 复制第三方项目源代码。

## 6. 对象与执行准确性

对象身份与复核基线固定为：

- Handle：跨会话主定位键；
- ObjectId：当前 AutoCAD 会话内执行键；
- entity type、layer、layout、bounding box 与 geometry fingerprint：对象复核；
- drawing revision：并发与陈旧读取校验；
- selection snapshot：用户选择证据；
- units、coordinate system、linear tolerance、angular tolerance：测量语义。

“第 3 条线”“左边那个块”“差不多在这里”或未冻结的“刚才选中对象”不是可靠身份。
未来任何写操作必须采用“前验 → revision → Handle + fingerprint → `DocumentLock` →
Transaction/Undo → 后验读取 → 几何/属性校验 → evidence → 失败回滚”。API 未抛异常不等于业务成功；
视觉预览只能作为第二证据。

## 7. 被拒绝方案

| 方案 | Phase 1 决定 | 原因 |
| --- | --- | --- |
| Python COM 生产主后端 | 拒绝 | 无法替代进程内 document context、managed transaction 与明确线程边界 |
| Named Pipe 第一版 | 拒绝 | 不增加首版 IPC 组合；loopback HTTP/JSON 更容易形成跨语言 contract 与测试替身 |
| gRPC 第一版 | 拒绝 | 首版不引入额外 codegen/runtime 复杂度 |
| ObjectARX 第一版 | 拒绝 | Managed .NET API 已满足 Phase 1 只读边界，避免原生 ABI 与 SDK 复杂度 |
| AutoLISP / command string / arbitrary script | 拒绝 | 执行面过宽，无法满足 operation allowlist 与结构化后验验证 |
| Core Console / APS Automation | 拒绝 | 不属于本机交互式 document-context Phase 1 |
| AutoCAD 2024 | 拒绝 | 需要独立 .NET Framework 4.8 兼容线，不混入 .NET 8 solution |

## 8. 开源参考与许可证结论

本轮只接受架构思想，不复制代码。HEAD 与许可证核验如下：

| 仓库 | 核验 HEAD | 许可证证据 | 复用决定 |
| --- | --- | --- | --- |
| [U-C4N/Autocad-MCP](https://github.com/U-C4N/Autocad-MCP) | `4a2e9d658f051cb756f92cb8fae945c64b8cc7d4` | [MIT LICENSE](https://github.com/U-C4N/Autocad-MCP/blob/main/LICENSE)，已核验 | 仅参考架构；未来复制仍需逐文件记录 |
| [puran-water/autocad-mcp](https://github.com/puran-water/autocad-mcp) | `95476a33a1c246308326eb4709d6379ef2efdbc1` | [MIT LICENSE](https://github.com/puran-water/autocad-mcp/blob/main/LICENSE)，已核验 | 仅参考 consolidated tool groups |
| [neka-nat/freecad-mcp](https://github.com/neka-nat/freecad-mcp) | `22a7d7b2c881779c0770029e4532be1e85c87ea1` | [MIT LICENSE](https://github.com/neka-nat/freecad-mcp/blob/main/LICENSE)，已核验 | 仅参考 in-application adapter 边界 |
| [frankhommers/autodesk-fusion-mcp](https://github.com/frankhommers/autodesk-fusion-mcp) | `3859d7e82faff70dcf056bd15be7e47c5cf912a0` | [MIT LICENSE](https://github.com/frankhommers/autodesk-fusion-mcp/blob/main/LICENSE)，已核验 | 仅参考宿主应用集成思路 |
| [daobataotie/CAD-MCP](https://github.com/daobataotie/CAD-MCP) | `352541820a56823568a90993dd773e7014205f44` | [MIT LICENSE](https://github.com/daobataotie/CAD-MCP/blob/main/LICENSE)，已核验 | 仅参考 controller 分层 |
| [nguyenngocdue/DeepBIM-MCP-Autocad-Plugin](https://github.com/nguyenngocdue/DeepBIM-MCP-Autocad-Plugin) | `efc443ec53bc22c293122523519d3195e83418b6` | GitHub license endpoint 未发现 LICENSE | 仅可参考公开架构；禁止复制代码 |
| [Joe-Spencer/fusion-mcp-server](https://github.com/Joe-Spencer/fusion-mcp-server) | `4dce1f696c20b4b9a40a0a5c4e168ec55b84d77f` | [GPL-3.0 LICENSE](https://github.com/Joe-Spencer/fusion-mcp-server/blob/main/LICENSE)，已核验 | 不复制进入 MIT 主仓 |
| [eyfel/mcp-server-solidworks](https://github.com/eyfel/mcp-server-solidworks) | `7fc7356b88f8947d1ee00375b1a9a43d21513a99` | GitHub repository metadata 为 `AGPL-3.0`；license endpoint 未返回文件 | 保守按 AGPL 边界处理，不复制 |

未来若复制或改写第三方代码，必须先重新读取目标 commit 的 LICENSE，记录来源仓库、commit、具体文件、
改写范围与版权要求。README 声明不等于代码质量，DXF 支持不等于原生 DWG 支持。

## 9. 未验证项与真实 AutoCAD 验证清单

以下事实本轮均为 `NOT VERIFIED`（未验证），不得在 capability 中宣称成功：

- AutoCAD 2025 与 2026 的实际插件 load/unload 行为；
- 目标机器上 `AcMgd.dll`、`AcDbMgd.dll`、`AcCoreMgd.dll` 的引用兼容性；
- `.bundle` / `PackageContents.xml` 的双版本安装与升级行为；
- modal command、busy document、document switch、关闭/退出时的 queue 行为；
- `DocumentLock`、Transaction、cancellation 与 timeout 在真实主线程中的失败模式；
- Golden DWG 的单位、布局、样式、块、属性、selection、Handle 与 geometry 读取结果；
- AutoCAD 进程崩溃、插件重载、端口冲突和断开恢复；
- PDF/PNG preview 的 fidelity、耗时与临时资源清理。

这些验证必须使用获许可的本机 AutoCAD、可丢弃 Golden DWG 和显式 opt-in integration profile。
GitHub-hosted runner 的 CI 绿色不能替代上述事实。

## 10. 来源清单

| 来源 | 核验日期 | 状态 |
| --- | --- | --- |
| [AutoCAD 2025 Developer and ObjectARX Help](https://help.autodesk.com/view/OARX/2025/ENU/) | 2026-07-17 | 官方入口 HTTP 200；真实插件行为未验证 |
| [AutoCAD 2026 Developer and ObjectARX Help](https://help.autodesk.com/view/OARX/2026/ENU/) | 2026-07-17 | 官方入口 HTTP 200；真实插件行为未验证 |
| [CAD-MAX reference project review](REFERENCE_PROJECTS.md) | 2026-07-17 | 已读取 |
| 上述 8 个 GitHub 仓库与 license metadata/LICENSE 路径 | 2026-07-17 | HEAD 与许可证结果已记录 |
| 用户提供的研究接受基线 | 2026-07-17 | 已接受为本轮约束；不是 runtime verification |
