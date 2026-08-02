# CADMAX-MCP-2026-07-28-STATELESS-MIGRATION / attempt-01

本文件是插入式高风险 MCP protocol migration 的 append-only evidence。它不决定 current authority，且只记录已经发生的脱敏事实；后续 candidate、CI、security 与 AutoCAD 结果只能追加，不能覆盖本节的 `NOT_RUN` 历史。

## 2026-08-02 / Authority reconciliation start

- Starting HEAD：`06eafa48eba175fb7d2466a7dba60f72d3931796`，当时 `HEAD == origin/dev`，branch 为 `dev`，工作区 clean。
- Initial authority：Phase 1.4 `ACCEPTED|CI_GREEN`，candidate `e62fded92df1064ffbd82b75cc910a568eb9fc7b`，CI run `30741291154`；Phase 1.5 `NOT_STARTED`。
- Authority reconciliation：已授权将 `PHASE_1_MAINTENANCE_MCP_2026_07_28_STATELESS_MIGRATION` 插入 Phase 1.4 与 Phase 1.5 之间；固定安全事实保持 `CONNECTED / dwg_read IMPLEMENTED / dwg_write NOT_IMPLEMENTED / read-only ENABLED / write-script DISABLED / LOOPBACK_ONLY`。
- Official SDK source：Python SDK stable tag `v2.0.0`，commit `6f69a3758ebf2ee55ce050f58b470ce11af71133`；核验日期 `2026-08-02`。
- MCP protocol revision：official specification tag `2026-07-28`，commit `5f5440bb26a62e2cf3440b92da5a667efa03b267`。
- Migration guide / what's new revision：Python SDK `v2.0.0` release commit；未依据博客或模型记忆实现。
- MCP SDK before：locked `mcp 1.28.1`，project range `mcp[cli]>=1.27,<2`。
- Pydantic before：locked `2.13.4`，project range `pydantic>=2.11,<3`。
- Reconciliation commit / CI：`NOT_RUN`。
- MCP SDK after / dependency resolution / vulnerability review：`NOT_RUN`。
- FastMCP → MCPServer / transport / authentication / tool schema / request-trace mapping：`NOT_RUN`。
- Explicit instance/document handles / backend cache policy / structured output：`NOT_RUN`。
- 2026 HTTP / stdio conformance / legacy fallback / multi-client isolation：`NOT_RUN`。
- Inspector / official SDK v2 Client：`NOT_RUN`。
- AutoCAD 2025 / AutoCAD 2026 smoke：`NOT_RUN`；不得以既有 Phase 1.4 evidence 代替本协议迁移 smoke。
- Security diff scan：`NOT_RUN`；既有 bearer server identity P3 继续为 documented limitation，不在本任务中误标关闭。
- Candidate commit / CI / closeout commit / CI：`NOT_RUN`。
- Known limitations：HTTP caller bearer token 仍只证明 token possession；write/script 开放前必须另行关闭 server identity limitation。
- Rollback：按逆序 `git revert` closeout、implementation/fix、authority reconciliation commits；不回滚 Phase 1.4，不使用 reset 或 force push。

## 2026-08-02 / Authority reconciliation pre-commit validation

- `git diff --check`：PASS。
- `scripts/docs/test-current-authority.ps1`：PASS；覆盖 Phase 1.4 accepted → maintenance、跳过 maintenance → Phase 1.5、maintenance accepted → Phase 1.5、固定安全事实、MCP v2 dependency 与 FastMCP import gates。
- `scripts/docs/verify-docs.ps1`：PASS；authority regression/current authority 与 66 个 Markdown relative links 均通过，warnings `0`、errors `0`。
- `scripts/verify.ps1`：PASS；Ruff、format、mypy、Python `78/78`、doctor、AutoCAD safety `29/29`、locked .NET restore/build/test 全部通过。
- .NET：Contracts `6/6`、Bridge Core `13/13`、Plugin `85/85`，build `0 warnings / 0 errors`。
- Review：未发现 authority skip、`dwg_read` 回退、write/script enable、source acceptance gate bypass 或 Phase 1.5 实现混入；状态为 `REVIEW_ACCEPTED|READY_TO_COMMIT`。

## 2026-08-02 / Authority reconciliation accepted

- Reconciliation commit：`0b4f03119d7dcbc4a13db384313af7ce65ec830f`（`docs(status): insert MCP 2026 stateless migration`）。
- Reconciliation exact-head CI：run `30748659252`，Governance、Python 3.12、.NET 8 均为 success；`HEAD == origin/dev` 后才开始本协议实现。

## 2026-08-02 / MCP v2 stateless implementation and pre-commit evidence

- Dependency resolution：project range 为 `mcp[cli]>=2,<3`、`pydantic>=2.12,<3`；locked runtime 为 `mcp 2.0.0`、`mcp-types 2.0.0`、`pydantic 2.13.4`。未单独 pin `mcp-types`；项目 Python→Bridge client 继续使用 `httpx 0.28.1`。
- CLI compatibility：`mcp version` 输出 `MCP version 2.0.0`。SDK v2 的 CLI 不支持旧式 `mcp --version`（exit `2`）；该命令不兼容事实已记录，未将其伪装为通过。
- Server API / transport：`FastMCP` 已迁移为官方 `MCPServer`；stdio 与 loopback Streamable HTTP 均保留，HTTP 显式 `stateless_http=true`、JSON response、caller bearer verification 与固定 `/mcp` path。2026-07-28 不需 initialize、支持 SDK 提供的 `server/discover`、不返回 `Mcp-Session-Id`；legacy `2025-11-25` initialize 流程使用同一 tool handler、auth、schema 与只读边界。
- Tool contract：inventory 精确为 `cad_system`、`drawing`；MCP input 已移除 `request_id`/`trace_id`，server 从安全的协议上下文映射或生成内部 correlation。公开 input/output 维持 closed schema 和结构化、脱敏失败 envelope。
- Explicit handle / cache policy：`status`、`list_documents` 不接收 document selector；`active_document` 只需 explicit instance；`units`、`bounds`、`layouts`、`system_metadata` 需 explicit instance 与 document selector。backend cache 仅优化且 instance/revision change 时清空，不能决定目标 document。
- Bridge server-side guard：security validation 发现直接 authenticated loopback Bridge call 可在旧实现中将 document metadata operation 与 null selector 送入 active-document resolution。修复后 C# validator 在 queue admission 前拒绝 `units`、`bounds`、`layouts`、`system_metadata` 的 null selector；`active_document` 保持 handle-discovery exception。该修复未改变 DWG read 业务数据语义。
- Bridge guard regressions：focused .NET test run `6/6` passed，覆盖四个 metadata operation 的 dispatcher rejection、`active_document` no-active-document 行为与 authenticated HTTP `400 / INVALID_ARGUMENT`；Python drawing/backend/protocol/isolation focused tests `48/48` passed。
- MCP conformance：Python test suite 包含 modern HTTP/stdio no-initialize、`server/discover`、tools/list/call、HTTP auth/version/body/schema rejection、legacy `2025-11-25`、structured output、restart、multi-client isolation；tools 仍精确两个。
- Inspector / official client：stable Inspector `2.0.0` modern stdio tools/list 与 `cad_system health` 通过；Inspector CLI 不暴露 `server/discover` method，official Python v2 client 已分别覆盖 discover。未使用真实 AI provider。
- Real AutoCAD smoke：AutoCAD 2025 与 2026 分别完成 read-only MCP 2026-07-28 HTTP/stdio基础调用；bridge/context/drawing doctor 均通过，显式 handles 传递，no session header，write/script false，DBMOD 保持 `0`，未保存 drawing，测试端口均释放。无 raw instance/document id、drawing 名称或路径记录。
- Dependency audit：runtime-only dependency audit 无已知漏洞。全开发环境 audit 仅识别既有 test-only `pytest 8.4.2` temporary-directory advisory（fixed `9.0.3`）；本批未升级无关 test dependency，作为 Low/P3 limitation 记录，不是 runtime exposure。
- Local full verification：`git diff --check` PASS；`uv sync --frozen`、Ruff、format、mypy 均 PASS；Python `89/89` PASS；doctor PASS 且显示 `readOnly=true`、`allowWrite=false`、`allowScript=false`、`httpHost=127.0.0.1`。locked .NET restore/build/test PASS，build `0 warnings / 0 errors`；Contracts `6/6`、Bridge Core `13/13`、AutoCAD Plugin `90/90`。
- AutoCAD harness：`Test-AutoCADScripts.ps1` `29/29` PASS；`Test-BridgeToken.ps1` `12/12` PASS；`Test-LoopbackBridge.ps1` PASS（loopback auth、bounded endpoints、port release）；`Test-DocumentContextDispatch.ps1` `22/22` PASS；`Test-ReadonlyDocumentInspection.ps1` PASS。47770 曾出现临时 bind conflict；确认没有 port exclusion/持久 listener 后可再次绑定，重跑 loopback script 通过，未改系统网络配置。
- Docs / full orchestration：`scripts/docs/verify-docs.ps1` PASS（authority errors `0`、links `66`、warnings `0`）；`scripts/verify.ps1` PASS。
- Security diff scan：sealed scan coverage `7/7` local-patch worklist rows，candidate discovery/validation/attack-path/remediation receipts complete，并生成 canonical report、独立 disclosure write-up、offline no-network control-flow probe 与 local-remediation hardening portfolio。pre-fix snapshot 的 document-selector gap generic severity 为 Low/P3（authenticated localhost/read-only），但按本 task 的 explicit-handle acceptance rule 属 P0 blocker；current implementation 已修复且回归验证。P0=`0`、P1=`0`。现有 Python→Bridge bearer server-identity P3-2 仍保持 OPEN，未误标关闭。
- Candidate commit / CI：`NOT_RUN`；closeout commit / CI：`NOT_RUN`。完成独立 review、提交、push 和 exact-head CI 后再追加新事实。

## 2026-08-02 / Independent implementation review

- Review scope：current uncommitted MCP v2/sessionless migration、Bridge server-side selector guard、Python/C# regressions与新增 protocol/isolation tests；review-only，未修改仓库。
- Review conclusion：`REVIEW_ACCEPTED|READY_TO_COMMIT`；P0=`0`、P1=`0`、P2=`0`、P3=`0`。
- Evidence：confirmed official `MCPServer` v2 API 与 locked `mcp 2.0.0` signature 一致；modern/legacy 共用 auth、middleware、tool handler与output schema；explicit handle chain 移除 `_document_id` correctness dependency；C# null-selector guard 位于 queue admission 前；未发现 Phase 1.5、entity/layer/block/selection/preview、write/script、COM/AutoLISP/command-string 混入。
- Review execution：独立 Python full test `89/89` PASS。reviewer 未独立重跑 .NET/docs/AutoCAD smoke 或 post-commit CI；这些仍必须由本轮已记录 validation 与后续 exact-head CI 共同证明。
- Known limitation：P3-2 Python→Bridge bearer server identity 继续 OPEN；CodeRabbit CLI 在当前 environment 不可用，reviewer 采用人工 source/diff review，不将其表述为 CodeRabbit result。

## 2026-08-02 / Candidate CI failure and focused repair

- Candidate `0ba282344013eda44268b307013f6f184d5cc6be` exact-head CI run `30755919741`：Governance=success、.NET 8=success、Python 3.12=failure，因此状态为 `COMMITTED|CI_FAILED|FIX_REQUIRED`，不得以部分成功接受 batch。
- RCA：only failing test 为 `test_stdio_2026_requests_need_no_initialize_and_survive_restart`。test hard-coded Windows console-script path `.venv/Scripts/cad-max-mcp.exe`；Linux CI console script 位于 current Python interpreter sibling `bin/cad-max-mcp`，导致 `FileNotFoundError`，不是 MCP protocol、auth、sessionless behavior或product runtime failure。
- Minimal fix：stdio test 由 `sys.executable` sibling resolve console script；Windows 使用 `cad-max-mcp.exe`，non-Windows 使用 `cad-max-mcp`。仍实际启动 console script/subprocess 和 official v2 client，不降级为 mock。
- Local fix verification：focused restart test PASS；Ruff check/format PASS；Python full suite `89/89` PASS。待 focused independent review、re-stage、fix commit、push后重新以 new exact HEAD 验证 CI。

## 2026-08-02 / Candidate CI repair review and revalidation

- Focused independent review：PASS，P0=`0`、P1=`0`、P2=`0`、P3=`0`。Windows resolves the console script beside `<venv>\\Scripts\\python.exe`; Linux/macOS resolves it beside `<venv>/bin/python`。test continues to spawn the real subprocess twice through official `stdio_client` and assert restart/discover/tools/list/call behavior；no test weakening。
- Revalidation：focused restart test PASS；Ruff check/format PASS；`scripts/verify.ps1` PASS。Python `89/89`、Contracts `6/6`、Bridge Core `13/13`、AutoCAD Plugin `90/90`；build `0 warnings / 0 errors`；doctor confirms read-only/write/script/loopback defaults；docs governance and AutoCAD script safety checks PASS。
- Next state：`REVIEW_ACCEPTED|READY_TO_COMMIT`。将以独立 fix commit 推送 `dev`，并只接受该 new exact HEAD 的 Governance、Python 3.12、.NET 8 all-success CI。
