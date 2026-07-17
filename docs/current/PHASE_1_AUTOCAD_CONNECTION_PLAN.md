# Phase 1 AutoCAD Connection 可执行计划

计划状态：`ACTIVE PLAN / NOT AUTHORITY`（活动计划 / 非当前状态 authority）
Task ID：`CADMAX-PHASE1-PLAN-AND-GOVERNANCE-SYNC`
适用平台：Windows、AutoCAD 2025/2026、.NET 8
执行模型：Phase + numbered work batch

本文定义 Phase 1 的可执行工程边界，但不决定当前 Phase。当前状态、accepted work batch、work batch
与唯一下一动作始终以 [STATUS.md](STATUS.md) 顶部 `cad-max-current-authority` 区块为准。

## 1. 背景

Phase 0 已建立 Python MCP、versioned JSON contract、C# Bridge Core/Host、SDK-free plugin boundary
与 fail-closed 安全默认值。Phase 1 将在这些边界内连接真实 AutoCAD 2025/2026，并交付受控、可分页、
可审计的只读检查能力。研究决策见
[AutoCAD MCP 研究接受基线](../research/AUTOCAD_MCP_RESEARCH_ACCEPTANCE.md)与
[ADR 0002](../adr/0002-autocad-2025-2026-mcp-runtime-baseline.md)。

## 2. 目标

- 使用 AutoCAD Managed .NET API 建立进程内 C# plugin boundary；
- 实现 plugin lifecycle、loopback bridge、token、health、heartbeat 与 dynamic capabilities；
- 建立 application/document context request queue、timeout、cancellation 与稳定错误映射；
- 只读检查文档、实体、图层、块、样式、选择集、Handle 和几何；
- 形成 preview、revision、fingerprint、evidence、audit 与 Golden DWG 验证体系；
- 保持 GitHub-hosted CI 不依赖 AutoCAD 或 Autodesk DLL；
- 每个 numbered work batch 独立 review、回滚、测试与 exact-head CI。

## 3. 非目标

- 不实现 create/update/delete/move/rotate/scale/mirror/offset/trim/extend；
- 不保存修改后的 DWG，不提供 purge/recover；
- 不注册 `WRITE`、`DESTRUCTIVE`、`SCRIPT` operation；
- 不使用 Python COM、AutoLISP、command string、arbitrary script；
- 不引入 ObjectARX、Core Console、APS Automation、RealDWG、ODA；
- 不支持 AutoCAD 2024、AutoCAD LT、其他 CAD、三维建模或云端多租户；
- 不把 mock、Bridge Host 或 CI 绿色描述为真实 AutoCAD/DWG 成功。

## 4. 固定架构

```text
MCP Host
  │ stdio / Streamable HTTP
  ▼
Python MCP Server
  │ 127.0.0.1 HTTP/JSON + token
  ▼
AutoCAD C#/.NET Plugin
  │ bounded document-context request queue
  ▼
AutoCAD Managed .NET API
  │ read transaction
  ▼
DWG
```

### 4.1 Python MCP 职责

- MCP transport 与少量顶层 tool group；
- JSON/Pydantic 参数 schema、枚举、分页上限和输入校验；
- `READ` / `PLAN` / `PREVIEW` 权限判定；
- Bridge URL loopback 校验、bounded timeout、cancellation；
- 客户端安全 error mapping 与 capability honesty；
- 不持有 Autodesk 类型，不直接访问 AutoCAD 或 DWG。

### 4.2 C# plugin 职责

- AutoCAD extension load/unload 与资源释放；
- loopback listener、token 验证、请求大小/并发/队列上限；
- application/document context routing 与主线程派发；
- `Document`、`Database`、read Transaction、Editor/SelectionSet；
- Handle/ObjectId 映射、revision/fingerprint、后验读取；
- AutoCAD exception 到稳定 error taxonomy 的转换；
- evidence/audit 元数据，不返回完整本机路径或原始异常。

## 5. 信任边界

| 边界 | 不可信输入 | 必需保护 | 禁止 |
| --- | --- | --- | --- |
| MCP client → Python | tool、operation、参数、分页、对象描述 | schema、allowlist、size/page limit、权限级别 | 任意 operation、任意路径、原始脚本 |
| Python → plugin | HTTP request、document/object selector | loopback、token、timeout、requestId/traceId、schemaVersion | 非 loopback、无 token、无限 retry |
| listener → request queue | 并发、取消、过期请求 | bounded queue、并发限制、deadline、backpressure | 无界线程/队列、在 listener 线程访问 AutoCAD |
| queue → AutoCAD | document/Handle/ObjectId | 主线程、context、revision、read transaction | 跨线程对象、长期 lock/transaction |
| plugin → client | result/error/evidence | DTO、redaction、stable error | Autodesk object、stack trace、路径、token、原始响应体 |

Loopback 不能替代本机进程间认证。token 必须由 machine-local 配置或受保护的进程启动通道提供，
不得进入仓库、URL、日志或客户端错误；插件重启时允许轮换。认证失败只记录 request/trace、错误码与耗时，
不记录 token 或原始 header。

## 6. AutoCAD 主线程与 document context

### 6.1 Context 模型

- listener 只做认证、解码、schema/version 检查和 enqueue；
- `application context` 只处理实例级状态与安全的 document routing；
- 文档读取进入目标 `document context`，并在 AutoCAD 支持的主线程执行；
- request 不得携带或缓存跨线程 Autodesk object；队列只传递不可变 DTO/selector；
- active document 在 enqueue 与执行之间可能变化，执行前必须重新解析并验证 revision。

### 6.2 `DocumentLock`

- Phase 1 只读 handler 不默认持有写锁；按 AutoCAD API/document context 的真实要求决定是否需要 lock；
- 必需时只锁定已授权目标文档，lock 范围不超过单个 handler 的最小读取窗口；
- 获取 lock 后不得等待网络、heartbeat、外部进程或另一个 request；
- 任何异常、取消、文档关闭或 shutdown 路径都必须释放 lock；
- lock 失败映射为 `DOCUMENT_LOCK_UNAVAILABLE`，不得改为读取其他文档。

### 6.3 Transaction

- 数据库读取使用最小 read transaction；
- 不跨 request 复用 Transaction、DBObject 或 ObjectId wrapper；
- cancellation 在 transaction 开始前和迭代分页边界检查；
- 资源释放使用 `using`/`try-finally`；
- Phase 1 不 commit mutation，不建立 Undo group，也不宣称 transaction rollback 已验证。

## 7. Request queue、timeout 与 cancellation

队列必须配置化并采用保守默认值，至少包含：

- `maxQueueDepth`、`maxConcurrentRequests`、`maxPageSize`、`maxResultBytes`；
- enqueue deadline 与 execution deadline；
- 每个 request 的 `CancellationToken`；
- 同一文档顺序执行，跨文档并发默认关闭，只有真实测试后才可评估；
- queue full 返回 `QUEUE_FULL`，不创建额外无界线程；
- deadline 已过的 request 在进入 AutoCAD 前丢弃并返回 `TIMEOUT`；
- client disconnect 触发 cancellation，但正在 AutoCAD 不可安全中断的原子读取只在安全点停止；
- shutdown 停止接收、取消待执行请求、等待有界 drain、释放 listener/heartbeat/订阅句柄。

不得无限重试。Python 对连接建立失败默认不自动重放业务读取；仅 heartbeat/reconnect 可以使用有限次数、
指数退避加抖动，并受总 deadline 约束。

## 8. Plugin lifecycle 与 connection state machine

```text
STOPPED
  → STARTING
  → LISTENING
  → READY
  ↔ BUSY
  → DEGRADED
  → STOPPING
  → STOPPED
```

- `STARTING`：读取安全配置、绑定 `127.0.0.1`、注册 lifecycle hook；
- `LISTENING`：listener 已启动，但 AutoCAD/document capability 仍可能不可用；
- `READY`：plugin、schema 与实例级 API 可用；不表示存在 active document；
- `BUSY`：modal/busy 状态暂时拒绝 document request；
- `DEGRADED`：heartbeat、document routing 或 capability probe 失败；
- `STOPPING`：拒绝新 request，执行有界 drain；
- `STOPPED`：端口、timer、queue、event subscription 已释放。

authority 中的 `autocad_runtime=NOT_CONNECTED` 只能在真实连接证据和 work-batch acceptance 后由后续任务修改；
计划或 mock 不改变该事实。

## 9. Heartbeat 与断开恢复

- plugin 提供无 DWG 副作用的 health/heartbeat；
- heartbeat 返回 pluginVersion、AutoCADVersion、schemaVersion、state、uptime 与 capability revision；
- Python 记录最后成功时间和 bounded latency，不记录路径/文档名；
- 进程退出、端口关闭、token mismatch、schema mismatch 分别映射，不吞成通用 `false`；
- reconnect 必须重新获取 capabilities，不沿用旧 ObjectId、selection snapshot 或 document revision；
- AutoCAD restart 后 Handle 可重新解析，ObjectId/session mapping 必须失效。

## 10. Dynamic capabilities

capabilities 以 plugin 当前真实状态动态返回：

```json
{
  "schemaVersion": "1.0",
  "capabilityRevision": "opaque-monotonic-value",
  "pluginState": "READY",
  "documentState": "NO_ACTIVE_DOCUMENT",
  "toolGroups": {
    "drawing": {
      "active_document": false,
      "list_documents": true
    }
  }
}
```

规则：

- operation 只有在 handler、失败测试和对应 runtime 条件均成立时才为 `true`；
- 无 active document 时，实例级 capability 可用，document/object capability 为 `false`；
- modal/busy、版本不兼容、schema mismatch、shutdown 会降低 capability；
- 未实现 operation 返回 `NOT_IMPLEMENTED`，不能返回空数据伪装成功；
- capability cache 必须短期、带 revision，并在 reconnect/document switch/plugin lifecycle 变化时失效。

## 11. Protocol contract

### 11.1 Request envelope

```json
{
  "schemaVersion": "1.0",
  "requestId": "uuid",
  "traceId": "uuid-or-safe-trace-token",
  "deadlineUtc": "RFC3339 timestamp",
  "toolGroup": "query",
  "operation": "list_entities",
  "permission": "READ",
  "document": {
    "documentId": "opaque-session-id",
    "revision": "opaque-revision"
  },
  "arguments": {}
}
```

HTTP token 放在专用 header，不进入 JSON。所有字符串、collection、pageSize、result bytes 和几何数组都必须
有上限。DTO 使用 JSON/Python/C# 一致的 camelCase；contract 不依赖 Autodesk 类型。

### 11.2 Response envelope

```json
{
  "schemaVersion": "1.0",
  "requestId": "uuid",
  "traceId": "uuid-or-safe-trace-token",
  "status": "OK",
  "result": {},
  "error": null,
  "evidence": {
    "documentRevision": "opaque-revision",
    "elapsedMs": 12
  }
}
```

分页响应必须包含稳定 sort key、`nextCursor` 与 `hasMore`。cursor 绑定 document revision、operation 与
filter fingerprint，revision 变化后返回 `STALE_REVISION`，不得静默继续混合快照。

## 12. Error taxonomy

| Error | 语义 | Retry |
| --- | --- | --- |
| `UNAUTHORIZED` | token 缺失/无效 | 否；修复配置 |
| `LOOPBACK_REQUIRED` | 非 loopback 请求/绑定 | 否 |
| `SCHEMA_MISMATCH` | contract 版本不兼容 | 否；协商/升级 |
| `NOT_IMPLEMENTED` | operation 未实现/未注册 | 否 |
| `NOT_CONNECTED` | plugin/AutoCAD 不可用 | reconnect 后新请求 |
| `NO_ACTIVE_DOCUMENT` | 无目标文档 | 用户打开/指定文档后新请求 |
| `DOCUMENT_NOT_FOUND` | opaque document selector 失效 | 重新列举 |
| `DOCUMENT_BUSY` | modal/busy | 有界退避后新请求 |
| `DOCUMENT_LOCK_UNAVAILABLE` | 无法取得所需 context/lock | 有界重试 |
| `STALE_REVISION` | revision 或 snapshot 已变化 | 重新读取 |
| `OBJECT_NOT_FOUND` | Handle 无法解析 | 重新读取 |
| `OBJECT_FINGERPRINT_MISMATCH` | 对象复核失败 | 禁止继续 |
| `QUEUE_FULL` | bounded queue 已满 | 有界退避 |
| `TIMEOUT` | deadline 到期 | 由调用方决定新请求 |
| `CANCELLED` | client/shutdown 取消 | 否 |
| `AUTOCAD_API_ERROR` | 已脱敏的 API 类别错误 | 依据 error code |
| `INTERNAL_ERROR` | 未分类且已脱敏错误 | 否；调查 |

错误响应不得包含 stack trace、原始异常消息、完整路径、document title、token、header 或原始 request/response。

## 13. Read-only tool groups

Phase 1 顶层 MCP 工具数量固定为 8 个；每组 operation 必须逐项注册并由 dynamic capabilities 证明：

| Tool group | Phase 1 operation | 等级 |
| --- | --- | --- |
| `cad_system` | `health`、`version`、`capabilities`、`connection_state`、`heartbeat` | `READ` |
| `drawing` | `active_document`、`list_documents`、`units`、`bounds`、`layouts`、`revision` | `READ` |
| `query` | `entity_count`、`count_by_type`、`list_entities`、`entity_summary`、`resolve_handle`、`measure_geometry` | `READ` |
| `layer` | `list_layers` | `READ` |
| `block` | `list_definitions`、`list_references`、`list_attributes` | `READ` |
| `style` | `list_text_styles`、`list_dimension_styles`、`list_linetypes` | `READ` |
| `selection` | `get_pickfirst` | `READ` |
| `preview` | `render_pdf`、`render_png` | `PREVIEW` |

通用参数 schema 包含 `operation` enum、document selector、page/pageSize 或 cursor、filter、sort、
units/tolerance 与最大结果限制。每个 operation 使用独立的 discriminated argument schema；不得用无结构
`object` 或脚本文本承载任意参数。未实现 operation 不进入 enum，或在跨版本请求时明确返回
`NOT_IMPLEMENTED`。

## 14. Handle、ObjectId、revision 与 fingerprint

只读实体概要至少包含：

- `handle`：规范化十六进制字符串，跨会话主定位键；
- `objectId`：opaque session-local token，不作为跨重启持久键；
- `entityType`、`layer`、`layout`；
- `boundingBox` 与 coordinate system；
- `geometryFingerprint`：类型相关、版本化、含 tolerance 语义的摘要；
- `drawingRevision`；
- `units`、`linearTolerance`、`angularTolerance`。

revision 由可验证的文档变更信号构成，具体算法必须在真实 AutoCAD work batch 中冻结；不得仅使用文件
mtime。fingerprint 不是密码学身份，也不能替代 Handle/revision；其目的为检测对象在读取与后续操作之间
发生变化。

## 15. Selection snapshot

`get_pickfirst` 只读取当前 PickFirst selection，不主动改变选择集。结果必须包含：

- opaque `selectionSnapshotId`、captured revision 与 captured time；
- 稳定排序的 Handle/ObjectId/session mapping；
- 每个对象的 type/layer/layout/bounds/fingerprint；
- selection 数量上限与 truncation 标记。

document switch、selection changed、revision changed 或 plugin reconnect 会使 snapshot 失效。模型不得仅凭
“刚才选中的对象”执行未来写操作。

## 16. Preview、evidence 与 audit

- PDF/PNG preview 只在明确 `PREVIEW` permission 下执行；
- preview 不保存 DWG mutation，不调用 command string，不覆盖用户文件；
- 临时输出放入受控、可清理目录，文件名随机且不含 drawing path/title；
- response 返回 digest、mime type、dimensions/page、revision、elapsedMs 与清理状态；
- preview failure 不改变 drawing/database；
- audit 记录 timestamp、requestId、traceId、operation、permission、document opaque id、revision、
  status/error code、elapsedMs、result count/truncated；
- audit 不记录 token、路径、完整对象 payload、DWG 内容或原始异常。

数据库级读取是第一事实证据；preview 是人类复核用第二证据，不能替代 Handle/revision/fingerprint。

## 17. Golden DWG 计划

Phase 1 后续创建可丢弃、无客户数据的 Golden DWG fixture；本 work batch 不创建 DWG。fixture 计划覆盖：

- metric/imperial units、model/paper layouts、可预测 extents；
- lines/arcs/circles/polylines/text/dimensions/hatches；
- on/off/frozen/locked layers 与稳定排序；
- block definition/reference、nested block、attributes；
- text/dimension/linetype styles；
- PickFirst selection、known Handles、known measurements；
- empty drawing、large bounded page、unsupported/proxy entity；
- AutoCAD 2025/2026 分别打开、断开、重启、文档切换。

fixture 必须明确许可证与生成方式；不得提交真实客户图纸。若二进制 fixture 进入仓库，必须在对应高风险
work batch 单独审查大小、来源、LFS/许可证与回滚，不在本计划任务中预先加入。

## 18. 测试分层

1. Contract tests：JSON Schema、camelCase、enum、error、分页、大小限制；无需 AutoCAD。
2. Python unit/integration：Bridge client loopback/token/timeout/cancellation/redaction；使用本地 fake server。
3. C# SDK-free tests：listener/dispatcher/queue/state machine/structured error；不引用 Autodesk DLL。
4. SDK compile profile：仅在本机显式提供获许可 SDK 时编译 adapter；默认 CI 跳过且清晰报告 `NOT_RUN`。
5. Real AutoCAD integration：self-hosted Windows、Golden DWG、2025/2026 matrix、load/unload、read results、
   disconnect/restart；不得在 GitHub-hosted runner 假装执行。
6. Security regression：non-loopback、无 token、wrong schema、queue overflow、oversize、path/log redaction、
   write/script registration absence。

每个 bug fix 必须增加正常、失败和边界回归。涉及 queue/cancellation 还需重复请求、shutdown 与资源释放测试。

## 19. CI 边界

GitHub-hosted required jobs 继续只运行：

- Governance：contract/checker/links/whitespace；
- Python 3.12：sync、Ruff、mypy、pytest、doctor；
- .NET 8：locked restore、SDK-free build/test。

不得在 GitHub-hosted runner 安装 AutoCAD、提交 Autodesk DLL、下载非官方 SDK、降低现有 analyzer/test，
也不得用 `continue-on-error` 伪造绿色。exact-head CI 只接受该 SHA 的所有 required jobs；candidate run
不能替代 closeout run。

## 20. Self-hosted Windows runner 边界

真实 AutoCAD integration runner 仅作为后续显式 opt-in、非公共环境计划：

- 使用隔离 Windows 账户、获许可 AutoCAD 2025/2026 与 disposable workspace；
- 不接受 fork PR，不执行不可信脚本，不暴露长期 token；
- runner label 精确区分 2025/2026，job 有 timeout、concurrency=1 和人工授权；
- Golden DWG 从受控只读源复制到每次运行目录，运行后销毁；
- AutoCAD/plugin 进程、端口、临时 preview、event subscription 必须在 finally 清理；
- 日志和 artifact 先脱敏，只保存 structured evidence；
- 该 runner 未建立前，所有 real integration 结果明确为 `NOT_RUN`。

## 21. SDK 引用计划

- `AcMgd.dll`、`AcDbMgd.dll`、`AcCoreMgd.dll` 只从本机获许可安装读取；
- machine-local SDK root 放入 Git 忽略的 props/config，不提交绝对个人路径；
- Autodesk references 使用 `Private=false` / Copy Local disabled；
- SDK-bound adapter 与默认 SDK-free project graph 隔离；
- build 在 SDK 缺失、版本不匹配、引用位于仓库、检测到二进制时 fail closed；
- AutoCAD 2025/2026 的条件引用和 API 差异必须由 compile matrix 与真实 load evidence 证明；
- 不从第三方镜像下载 DLL，不把 Autodesk EULA/许可证内容复制进日志或仓库。

## 22. `.bundle` 与安装计划

`PHASE_1_1` 只设计并验证开发用 `.bundle`：

- `PackageContents.xml` 明确 SeriesMin/SeriesMax、module path、load reason 与版本；
- 2025/2026 是否共用 bundle 由真实加载验证决定，不能只凭编译推断；
- install/uninstall/update 使用用户可控目录，不修改系统配置或注册表，除非未来任务单独授权；
- 安装前检查目标、占用与旧版本；失败保留可回滚状态；
- 不把本机安装路径、Autodesk DLL 或生成 bundle 混入本计划提交。

## 23. 风险与缓解

| 风险 | 影响 | 缓解/停止条件 |
| --- | --- | --- |
| SDK 引用不安全 | 专有 DLL/路径进入 Git | binary/path scan；发现即 `BLOCKED / SDK_REFERENCE_UNSAFE` |
| 插件崩溃 AutoCAD | 用户进程/图纸受影响 | 小 handler、disposable DWG、逐 batch load test；异常即停止 |
| 主线程/文档上下文错误 | hang、错误文档读取 | bounded queue、执行前 document/revision 重验、modal/busy error |
| listener 暴露 | 本机或网络未授权访问 | 127.0.0.1、token、bind validation、no tunnel |
| 大图纸内存/耗时 | AutoCAD 卡顿或 OOM | paging、max count/bytes、stable cursor、deadline、no full scan default |
| capability 夸大 | MCP 误判能力 | operation 逐项 enable；未实现 fail closed |
| preview 临时资源泄漏 | 磁盘/隐私风险 | 受控目录、digest/redaction、finally cleanup |
| CI 误当真实验证 | 发布错误事实 | hosted vs real integration 分层，`NOT_RUN` 明示 |

## 24. 回滚策略

每个 work batch 独立提交并可使用 `git revert <sha>` 回滚。运行时回滚还包括：

- 停止 listener、取消 queue、卸载 plugin；
- 恢复上一版本 `.bundle` 或移除开发 bundle；
- 将 capability 关闭并返回结构化 unavailable/error；
- 保持 `readOnly=true`、`allowWrite=false`、`allowScript=false`；
- 不以删除检查、force push、reset shared history 或假成功作为回滚。

任何疑似 DWG mutation、Autodesk binary 泄漏、non-loopback bind、token 泄漏、主线程不安全或资源未释放均为
立即停止条件。

## 25. Numbered work batches

每个 batch 只有一个主要目标，控制在 1 至 2 个工作日，可独立 review、回滚、测试和 exact-head CI；
未 accepted 前不得启动下一 batch。

| 顺序 | Work batch | 主要交付 | 明确排除 | Most likely |
| --- | --- | --- | --- | --- |
| 0 | `PHASE_1_PLAN` | research acceptance、ADR、治理同步、本主计划 | runtime、DWG | 1 天 |
| 1 | `PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP` | 2025/2026 .NET 8 项目边界、本机 SDK 引用、`.bundle`、load/unload | 图纸读取 | 1 天 |
| 2 | `PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE` | plugin 内 127.0.0.1 HTTP、token、health/version/capabilities、start/stop、端口/退出清理 | document API | 1 天 |
| 3 | `PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH` | application/document context queue、主线程、active document routing、timeout/cancellation/heartbeat、modal/busy | 内容读取 | 1.5 天 |
| 4 | `PHASE_1_4_READONLY_DOCUMENT_INSPECTION` | runtime/document list/active document/units/bounds/layouts/system metadata | DWG mutation | 1 天 |
| 5 | `PHASE_1_5_READONLY_OBJECT_INSPECTION` | entities/layers/blocks/attributes/styles、paging、stable sorting、DTO/schema | selection/preview | 1.5 天 |
| 6 | `PHASE_1_6_SELECTION_AND_ACCURACY` | PickFirst、Handle、ObjectId、revision、fingerprint、bounds、measurement、tolerance | write target execution | 1 天 |
| 7 | `PHASE_1_7_PREVIEW_EVIDENCE_GOLDEN_DWG` | PDF/PNG preview、audit/evidence、Golden DWG harness、2025/2026 matrix、restart | 客户 DWG、write | 1 天 |
| 8 | `PHASE_1_CLOSEOUT` | integration evidence、capability honesty、安全 review、exact-head CI、authority closeout | 下一 Phase implementation | 1 天 |

Phase 1 总工程估算：most likely（最可能）10 个工作日，conservative（保守）15 个工作日。该估算不是
承诺交付日期；假设是获许可 AutoCAD 2025/2026、SDK、本机测试时段和 self-hosted/人工验证资源可及时使用。
SDK/API 差异、插件崩溃、modal/thread 问题或 runner 不可用会消耗保守余量。

## 26. 每个 work batch 的通用验收

- authority 与 next action 在开始前匹配；
- target/excluded files、risk、docs budget 明确；
- 正常、失败、边界测试通过；
- 没有 Autodesk binary、凭证、`.env` 或绝对个人路径；
- 8 个安全 tool group 之外无扩张，未实现 operation 不注册；
- local full verify 通过，高风险 review 为 `REVIEW_ACCEPTED|READY_TO_COMMIT`；
- commit/push 后 `HEAD == origin/dev`；
- 该 exact HEAD 的 Governance/Python/.NET required jobs 全绿；
- real AutoCAD 未运行时明确写 `NOT_RUN`，不伪造；
- attempt evidence 按两位序号追加，不覆盖旧 attempt。

## 27. Phase 1 closeout 标准

Phase 1 只有在以下条件全部满足时才能 closeout：

- AutoCAD 2025 与 2026 plugin load/unload 有真实 evidence；
- loopback/token/lifecycle、document queue、timeout/cancellation/heartbeat 均有失败测试；
- 所列只读 operation 的 dynamic capability 与真实 handler 一致；
- Golden DWG matrix 覆盖 document/object/selection/accuracy/preview 与 disconnect/restart；
- 分页、稳定排序、result limits 与大图边界验证完成；
- Handle/ObjectId/revision/fingerprint/tolerance contract 冻结；
- write/script/destructive operation 仍未注册；
- security review 无 P0/P1，Autodesk binary/secret/path scan 通过；
- GitHub-hosted required CI 与受控 real integration evidence 分别真实记录；
- Phase closeout candidate 与 authority closeout exact-head CI 均成功；
- `STATUS.md`、current `ROADMAP.md` 与 evidence 一致；
- 下一 Phase 仅完成规划授权，不在 closeout 混入实现。

若任一项缺失，Phase 1 保持 `IN_PROGRESS|NOT_FROZEN`，并输出精确 blocker；不得以 preview、mock、
SDK-free test 或 CI 绿色替代真实 AutoCAD evidence。
