# CADMAX-PHASE1-4-READONLY-DOCUMENT-INSPECTION / attempt-01

## Task classification

`CODE_CHANGE / AUTOCAD_RUNTIME / DWG_READ / DOCUMENT_INSPECTION / READ_TRANSACTION / PROTOCOL_CONTRACT / SECURITY_BOUNDARY / TEST`

Risk classification：`HIGH_RISK`。本 attempt 只记录已经发生的 SDK-free implementation 和验证；它不接受
work batch，不推进 authority，也不把 mock、CI 或源码审查描述为真实 DWG 读取。

## Starting facts and scope

- Starting HEAD：`868eaa4474ae4bdad7601bd19ae9d6f21a151b73`；branch `dev`；开始时
  `HEAD == origin/dev`。
- Initial authority：schema 2；`PHASE_1 / IN_PROGRESS|NOT_FROZEN`；accepted batch
  `PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH / ACCEPTED|CI_GREEN`；current batch
  `PHASE_1_4_READONLY_DOCUMENT_INSPECTION / NOT_STARTED`；唯一 next action 为
  `IMPLEMENT_PHASE_1_4_READONLY_DOCUMENT_INSPECTION`。
- Target：在既有 authenticated loopback 和 bounded dispatcher 上新增唯一
  `POST /v1/drawing/inspect`，固定 operation 为 `status`、`list_documents`、`active_document`、
  `units`、`bounds`、`layouts`、`system_metadata`。
- Excluded：entity/layer/block/style/selection/Handle/ObjectId enumeration、revision、write/script、
  command string、COM、DocumentLock、OpenMode.ForWrite、UpdateExt、save、真实客户 DWG、token 和本机路径。

## Read-only boundary and protocol

- application context 仅处理 `status` / `list_documents`；其余 operation 使用既有 active-document
  command context，执行时重新解析 active document，不主动切换 document。
- layouts 使用短生命周期 `StartOpenCloseTransaction` 与 `OpenMode.ForRead`；transaction 不跨 request、
  await 或网络调用。bounds 只投影当前 Database extents，不触发 extents 更新、regen 或 entity recompute。
- response 只包含 allowlisted document metadata、有限数值、opaque document identity 和 read-only
  evidence；basename/layout/current-layout name 均拒绝 separator、colon、control characters、`.` 与 `..`。
- Python 以严格 Pydantic schema 验证 response，绑定 instance/document identity，禁用 HTTP proxy
  environment inheritance，并将 bridge diagnostics 归一为 client-owned safe text。
- MCP 顶层 tool 仍精确为 `cad_system` 与 `drawing`；write/script/object operation 保持 false。

## Security review and P3 handling

- recursive production-source scan 对 `OpenMode.ForWrite`、upgrade/downgrade、save、UpdateExt、
  command string、DocumentLock、selection、Handle 和 ObjectId 均为 `ABSENT`；known-bad fixture
  regression 同时通过。
- P3-1 implementation closure：token provisioning/revalidation 对非 allowlisted control rights fail closed；
  C# listener token loader 改为在同一 reparse-resistant native handle 上检查 identity、ACL 和内容，避免
  pathname check/read TOCTOU。C# token tests 与 PowerShell token harness 均 PASS。
- P3-2：仍是 documented local-server identity limitation；保持 authenticated loopback、read-only、
  write/script disabled，未设计或声称 TLS/named-pipe identity fix。当前 complete scan 不会被重开；本次
  代码变更后的完整 diff scan 为 `NOT_RUN`，必须在 candidate 前重新执行。
- `bridge-doctor` 现在逐项报告 health 的 `documentAccess` / `dwgRead`，不再以 aggregate connection
  state 推断；MCP inventory 和 security documentation 与七项 drawing enum 保持一致。

## SDK-free validation

- `git diff --check`：PASS。
- `scripts/verify.ps1`：PASS；locked dependency sync、Ruff、format、mypy、Python `78/78`、doctor、
  PowerShell safety/doc governance、locked .NET restore、Release build `0 warnings / 0 errors` 与
  .NET Contracts `6/6`、Plugin `85/85`、Bridge Core `13/13` 均 PASS。
- `Test-ReadonlyDocumentInspection.ps1`：PASS；current-source rebuild、read-only source boundary、
  Contracts `1/1`、Plugin `15/15`、Phase 1.4 Python `49/49` 均 PASS。
- `Test-BridgeToken.ps1`：PASS；`12` 个 token/ACL/redaction cases。`Test-AutoCADScripts.ps1`：PASS；
  `28` 个 SDK/bundle/token/harness safety cases。
- 2025 / 2026 installation detection：两者均检测到，Managed API version 与 .NET 8 boundary 均通过；
  此检测不等于 plugin load 或 DWG read runtime evidence。

## Real AutoCAD, candidate, and closeout

- AutoCAD 2025 matrix：`NOT_RUN`。需要用户通过 UI 准备可丢弃 fixture、确认 active document、输入
  DBMOD-before / DBMOD-after，并在不保存关闭后允许 hash verification。
- AutoCAD 2026 matrix：`NOT_RUN`，要求与 2025 相同且独立执行。
- DBMOD / DWG SHA-256 / no-Undo evidence：`NOT_RUN`；本 attempt 未启动 AutoCAD、未安装 bundle、未创建、
  未修改、未保存或读取任何 DWG。
- Implementation commit、candidate exact-head CI、closeout commit、closeout exact-head CI：均为
  `NOT_RUN`。authority 保持 `PHASE_1_4_READONLY_DOCUMENT_INSPECTION / NOT_STARTED`，
  `dwg_read=NOT_IMPLEMENTED`。

## Known limitations and rollback

- 未运行真实 AutoCAD 2025/2026 的 UI matrix、独立 full current diff security scan 和 GitHub exact-head CI；
  这些是 acceptance blocker，不由 SDK-free PASS 代替。
- 未产生 runtime install、bundle、AutoCAD process 或 DWG side effect。需要回退时，只对本 attempt 的
  源码/测试/文档变更逐项反向 patch；不得 reset、clean 或覆盖已有工作区改动。

## Continuation evidence / 2026-07-27 real runtime and security closure

本节追加真实验收与后续安全修复事实；上文的 `NOT_RUN` 保留为较早时间点的历史状态，不再代表本
attempt 的最新结论。所有值均为脱敏状态、布尔值或计数，不记录 fixture、document、identity、token、
本机路径、原始 DBMOD 或原始 SHA-256。

### AutoCAD 2025

- Full document inspection matrix：`PASS`。
- `drawing-doctor`、`status`、`list_documents`、`active_document`、`units`、`bounds`、`layouts`、
  `system_metadata`：均为 `PASS`。
- `DBMOD_UNCHANGED=true`；`SHA256_UNCHANGED=true`；`PATH_SENTINEL_ABSENT=true`。
- Final `queueDepth=0`；final `inFlight=0`；port release：`PASS`；UI close without save：`PASS`。

### AutoCAD 2026

- Full document inspection matrix：`PASS`。
- `drawing-doctor`、`status`、`list_documents`、`active_document`、`units`、`bounds`、`layouts`、
  `system_metadata`：均为 `PASS`。
- `DBMOD_UNCHANGED=true`；`SHA256_UNCHANGED=true`；`PATH_SENTINEL_ABSENT=true`。
- Final `queueDepth=0`；final `inFlight=0`；port release：`PASS`；UI close without save：`PASS`。

### Security diff scan and P3 disposition

- Full working-tree diff scan：`5e6e5fe6-2014-42a3-b9c4-8f1f73b81030`；durable status：`complete`。
- Finding counts：`P0=0`、`P1=0`、`P2=0`、`P3/Low=1`。
- P3-1 token ACL：`CLOSED`。
- P3-2 bearer sent before authenticated server identity：`DOCUMENTED`；trust boundary 继续限定为
  authenticated loopback、read-only、write/script disabled，启用 write/script 前必须关闭，不声明已修复。
- 新增 Low/P3 child-output finding：`FIXED`。四个 SDK-free child process 的 stdout/stderr 由 harness
  捕获并直接排空，不转发、不持久化，只保留 exit code 与稳定错误码。
- Remediation verification：PowerShell parse `PASS`；script safety `29/29 PASS`；readonly harness
  `exitCode=0`、`outputRecordCount=1`、structured result `PASS`、path-like output `false`。
- 未重复执行第三次全量扫描；完整扫描覆盖与仅限上述 P3 remediation 的聚焦验证共同构成 candidate
  安全证据链。

## Continuation verification / 2026-08-02

- `git diff --check`：`PASS`。
- `uv sync --frozen`、Ruff lint、Ruff format、mypy：`PASS`；Python：`78/78 PASS`。
- locked .NET restore：`PASS`；Release build：`0 warnings / 0 errors`；Contracts `6/6`、Bridge Core
  `13/13`、Plugin `85/85`：均为 `PASS`。
- `Test-BridgeToken.ps1`：`12/12 PASS`；`Test-AutoCADScripts.ps1`：`29/29 PASS`；
  `Test-DocumentContextDispatch.ps1`：`18/18 PASS`；`Test-ReadonlyDocumentInspection.ps1`：`PASS`。
- `Test-LoopbackBridge.ps1`：冷启动首次与一次确认重跑均返回稳定
  `ERROR / BRIDGE_START_TIMEOUT`；受控等价 Host/port/token 探针随后 `PASS`，原脚本预热重跑
  `PASS`，authentication、doctor classification 与 port release 均通过。未修改代码来掩盖该失败。
- 统一 `scripts/verify.ps1`：`PASS`；documentation governance：`PASS`。
- AutoCAD 已按真实验收流程关闭，因此独立 `bridge-doctor`、`context-doctor`、`drawing-doctor`
  当前返回 `BACKEND_NOT_CONFIGURED`；对应 live checks 已由上文 2025/2026 full harness `PASS` 覆盖，
  本 continuation 未重新启动 AutoCAD 或访问 DWG。
