# CADMAX-PHASE1-3-DOCUMENT-CONTEXT-DISPATCH / attempt-01

## Task classification

`CODE_CHANGE / AUTOCAD_RUNTIME / DOCUMENT_CONTEXT / MAIN_THREAD_DISPATCH / PROTOCOL_CONTRACT / SECURITY_BOUNDARY / TEST / CI_CD / GITHUB_PUBLISH`

Risk classification：`HIGH_RISK`；本批次引入 bounded context queue、AutoCAD application/document
command context、短生命周期 Document identity、deadline/cancellation、modal/busy 和 shutdown/restart。
接受前必须完成 AutoCAD 2025/2026 真实矩阵、完整 security scan、full verify、implementation 与
closeout 两轮 exact-head CI，且 `P0=0 / P1=0`。

## Starting facts and scope

- Starting HEAD：`3d0f20b8c00b789968a44f129834de3122a15cf6`；branch `dev`；开始时
  `HEAD == origin/dev`。
- Initial authority：schema 2；`PHASE_1 / IN_PROGRESS|NOT_FROZEN`；accepted batch
  `PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE / ACCEPTED|CI_GREEN`；current batch
  `PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH / NOT_STARTED`；唯一 next action
  `IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH`。
- Target：wire contract、SDK-free bounded dispatcher、SDK-bound AutoCAD adapter、唯一 production
  `POST /v1/context/probe`、dynamic context capability/heartbeat、Python backend/context-doctor、tests、
  runtime harness 与本 attempt evidence。
- Excluded：Document name/path/window/content、Database、Transaction、DocumentLock、Editor input/selection、
  ObjectId、Handle、DWG read/write/open/save、command/script/AutoLISP/COM、document switching/creation by
  plugin、multi-instance routing 与 Phase 1.4。
- Phase 1.2 固定事实保留：production `127.0.0.1:47770`、development `127.0.0.1:47779`、bearer
  token required、四个 process endpoint、production `/v1/commands` absent、shutdown active task `0`。

## Queue and dispatcher design

- SDK-free `DocumentContextDispatcher` 使用最大深度 `32` 的 FIFO；同一时刻最多一个 in-flight；
  `TaskCompletionSource` 使用 `RunContinuationsAsynchronously`；completion、cancellation 和 timeout 均为
  exactly-once state transition。
- enqueue 前验证 schema、request/trace/instance/document ID 与 future deadline；deadline 最大 `10s`；
  queue full=`QUEUE_FULL`，过期=`TIMEOUT`，disconnect/取消=`CANCELLED`，stopping=`BRIDGE_STOPPING`。
- queued cancellation/timeout 会从队列安全移除或在 drain 前跳过；不 retry、不 thread abort、不创建
  per-document queue。
- Idle drain 固定 `maxItemsPerIdle=4`、`maxDrainDuration=25ms`、one in-flight；达到数量/时间预算即返回；
  不同步等待 listener/Python，不在 callback continuation 执行不受控 AutoCAD 工作。
- `DocumentCommandContextCoordinator` 验证 main thread，通过 SDK-bound scheduler 调用官方
  `DocumentCollection.ExecuteInCommandContextAsync`；scheduler/callback 错误映射为稳定错误，不泄露异常。

## Application/document context and events

- adapter 在 Initialize 订阅 `Application.Idle/EnterModal/LeaveModal/BeginQuit`，以及
  `DocumentCreated/DocumentActivated/DocumentBecameCurrent/DocumentActivationChanged/
  DocumentToBeDestroyed/DocumentDestroyed`。
- Terminate 先关闭 admission，再完整解除事件、abandon in-flight、reset identity；重复 Initialize/
  Terminate 安全；event handler 极短、无网络 IO、异常不逃逸 AutoCAD。
- 执行时重新读取 `MdiActiveDocument`，不以 enqueue 时 snapshot 为权威；busy 只读取 allowlisted
  `Editor.IsQuiescent`；插件不主动切换或创建文档。
- modal depth 受控且不小于零；modal admission/queued work fail closed 为 `APPLICATION_MODAL`；busy 为
  `DOCUMENT_BUSY`；zero document 为 `NO_ACTIVE_DOCUMENT`。

## Opaque document identity

- 每个 live `Document` 获得进程内随机 `doc_<base64url 128-bit>`；正向映射使用
  `ConditionalWeakTable`，反向映射使用 weak reference。
- same live document stable、different documents distinct；destroy 时显式 remove/prune；restart/reset 后
  全部失效且不复用；expected ID 仅允许等于执行时 active document，否则 `DOCUMENT_NOT_ACTIVE` 或
  `DOCUMENT_NOT_FOUND`。
- request/response/heartbeat 不包含 title、path、window、Database、ObjectId、Handle、Editor data 或
  command name；仓库 evidence 不保存真实 document ID。

## Protocol, Python, and capability boundary

- production 只新增 `POST /v1/context/probe`；request body/schema/extra fields/deadline/instance/document ID
  fail closed；未新增 `/v1/commands`、generic dispatch、script 或 evaluate route。
- success 必须包含 `mainThreadVerified=true`、`executionContext=DOCUMENT_COMMAND_CONTEXT`、opaque
  document ID、bounded timing；失败使用稳定 HTTP/status/error taxonomy，原始异常、header、token、response
  body 和本机路径不进入响应。
- heartbeat 只增加 dispatcher state、queue/in-flight、modal、has-active-document 和稳定 last status；
  不含 document ID。dynamic capability 只开放 `bridge.contextDispatch`、`bridge.contextProbe` 与诊断性
  `documentContext.available`；drawing/query/layer/block/style/selection/preview/dwg/write/script/command
  继续 false。
- Python 继续扩展既有 `AutoCadBridgeBackend`；loopback-only、bounded timeout/response、schema/error
  validation；instance rotation 与 destroyed status 清空 opaque document cache；busy/modal/timeout 不 retry。
- `context-doctor` 执行 health、两次 probe、main-thread/command-context/document-format/stability 检查；
  MCP 顶层工具仍精确为 `cad_system` 与 `drawing`。

## SDK-free and focused validation

- Queue tests覆盖 FIFO、bounded/full、one in-flight、enqueue/queued timeout、queued/in-flight cancellation、
  shutdown reject/drain、repeat start/stop、10,000 次 stress、continuation fault、metrics。
- Dispatcher/identity tests覆盖正确/错误线程、scheduler/callback failure、no document、mismatch/destroy、
  modal/busy、timeout/cancellation、shutdown、callback/completion exactly once、stable/distinct/prune/reset、
  weak retention 与无 name/path leakage。
- Focused Python：`17/17 PASS`；focused .NET：Contracts `5/5`、Plugin `66/66`、Bridge Core `13/13`，
  合计 `84/84 PASS`；AutoCAD PowerShell safety `25/25 PASS`；`git diff --check` PASS。
- 最终工作区的 `scripts/verify.ps1` PASS：Python `41/41`；.NET Contracts `5/5`、Plugin
  `66/66`、Bridge Core `13/13`，合计 `84/84`；AutoCAD safety `25/25`；Release build
  `0 warnings / 0 errors`；Ruff、format、mypy、doctor、locked restore、docs authority/link governance 全部
  PASS。Token safety `10/10` 与 development loopback/port release 也独立 PASS。

## Real AutoCAD 2025 validation

- 本机检测到并真实运行 AutoCAD 2025；SDK-bound adapter/bundle build/load、status、bridge doctor、
  context-doctor、main thread 与 document command context 均 PASS。
- 两个 UI 创建的 disposable blank documents：same-document ID stable、A/B distinct；B active + expected A
  为 `DOCUMENT_NOT_ACTIVE`，切回 A + expected A 成功；destroyed A 为 `DOCUMENT_NOT_FOUND`。
- modal=`APPLICATION_MODAL` 且关闭后恢复；manual LINE busy=`DOCUMENT_BUSY` 且取消后恢复；
  timeout=`TIMEOUT`；disconnect cancellation=`CANCELLED`；saturation=`QUEUE_FULL`；每项结束 queue/in-flight
  均回到 `0`。
- 关闭全部测试文档得到 `NO_ACTIVE_DOCUMENT`，重建空白文档后恢复；正常 shutdown 在 deadline 内退出，
  47770 释放；restart 的 instance ID 轮换、全部旧 document ID 失效、context/doctor 恢复。
- shutdown 与并发 saturation 发生重叠，但 detached client 未留下可独立观察的 pending response status；
  正常 BeginQuit/drain、进程退出、port release 和 restart 已独立实测，不把不可观察项写成 PASS。

## Real AutoCAD 2026 validation

- 本机检测到并真实运行 AutoCAD 2026；plugin 为 `READY`，`autoCADYear=2026`，read-only true，
  write/script false；bridge doctor、context-doctor、main thread 与 command context PASS。
- 两个 UI 创建的 disposable blank documents 完成 stable/distinct ID、wrong-active fail closed、switch-back
  success；modal/busy 及恢复、timeout、disconnect cancellation、queue saturation 全部 PASS；结束时
  queue/in-flight 均为 `0`。
- 关闭 active document 后旧 ID=`DOCUMENT_NOT_FOUND`；关闭全部 document 后
  `NO_ACTIVE_DOCUMENT`；UI 重建 blank document 后 context probe 恢复。
- 首次正常 shutdown：认证/capability honesty/bridge doctor/47770 release PASS。restart 后 instance ID
  轮换，重启前保存的三组 opaque ID 全部 `DOCUMENT_NOT_FOUND`，新 document ID 与 context-doctor 恢复。
- restart 实例再次正常退出；`restartInstanceChanged=true`、process `0`、listener `0`、port release PASS。
  harness 的 `mode` 字段历史上硬编码为 `REAL_AUTOCAD_2025_BRIDGE`，本节仅依据同一脚本实际连接到
  `autoCADYear=2026` 的 health/doctor 结果，不改写该已知标签缺陷。

## Security review

- Standard Codex Security scan `779c3ee1-52b5-41f6-a907-a54a31d7e75f` 完成；discovery receipts
  `51/51`；canonical findings `2`；结论 `P0=0 / P1=0 / P2=0 / P3=2`。
- P3-1：token file ACL 校验未拒绝非 owner 的 `WRITE_DAC/WRITE_OWNER` control rights。它是既有 Phase 1.2
  bearer-token boundary limitation；当前 context endpoint 保持 read-only，但未来 write/script 前必须修复。
- P3-2：Python loopback client 在验证 peer identity 前发送 bearer token。当前固定 127.0.0.1、token ACL、
  server fail-closed 限制影响，但这不是独立的 authenticated server identity；未来高权限能力前需采用 pinned
  local TLS 或 protected named-pipe/session authentication。
- 两项均有独立 write-up/PoC；没有静默忽略。Report SHA-256：
  `336BCF39BFE90F25E64D1234CEC1785AD89F7FD6585DCD2012AF53A51F51F8F2`。
- 扫描未把真实 AutoCAD runtime、credential value 或 external service 当作已验证；这些事实由本 attempt 的
  独立实机验收提供。

## Candidate and closeout

- Implementation commit：`UNCOMMITTED`；目标 message：
  `feat(autocad): add document context dispatch`。
- Candidate exact-head CI：`NOT_RUN`；必须验证 Governance、Python 3.12、.NET 8 全部 success，且 run
  head SHA 精确等于 candidate。
- Closeout commit/run：`NOT_RUN`；candidate CI GREEN 后才允许推进 authority 到 Phase 1.4；closeout
  自身还需 exact-head CI GREEN。
- 当前 authority 保持 Phase 1.3 `NOT_STARTED`，未用本机 PASS 或 security scan 提前推进。

## Known limitations

- AutoCAD dev bundle 未签名；每次新进程首次 demand-load 时 AutoCAD 会显示 publisher/security prompt。
  本次只在用户明确授权后选择一次性 load，没有修改 `SECURELOAD`、`TRUSTEDPATHS`、注册表或系统安全设置。
  持久消除提示需要可信代码签名/受控发布方案，单独评审，不属于本 batch。
- shutdown-pending 的 detached client response 在 2025 并发尝试中不可独立观察；已验证 BeginQuit admission
  close、bounded dispatcher drain、normal process exit、port release 与 restart。该限制不隐藏为完整 pending
  response evidence。
- 两个 security P3 属本地 bearer trust boundary；当前 read-only、loopback-only 与 write/script disabled
  保持，但 Phase 1.4 以后扩大能力前应排期修复。

## Rollback

- 未提交：只反向修改本 attempt target files；不 reset/clean/checkout，不覆盖用户已有改动。
- 本机：正常关闭 AutoCAD，精确卸载当前 2025/2026 dev bundle，确认 47770 释放，再恢复 Phase 1.2 bundle；
  `bridge-doctor` PASS 且 context capabilities false。
- 已推送后：按逆序 `git revert <closeout-sha>`、`git revert <candidate-sha>`；不改写共享历史。
