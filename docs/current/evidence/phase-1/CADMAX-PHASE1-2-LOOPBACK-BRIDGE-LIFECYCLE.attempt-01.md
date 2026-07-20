# CADMAX-PHASE1-2-LOOPBACK-BRIDGE-LIFECYCLE / attempt-01

## Task classification

`CODE_CHANGE / AUTHENTICATED_LOOPBACK / AUTOCAD_RUNTIME / SECURITY_FIX / TEST / CI_CD / GITHUB_PUBLISH`

Risk classification：`HIGH_RISK`；涉及机器本地 bearer token、真实 AutoCAD 2025/2026 进程内
listener、当前用户 `.bundle` 安装与 authority 推进。接受前必须完成 focused security review、真实版本
矩阵、端口冲突、shutdown/reconnect 与 candidate exact-head CI，且 `P0=0 / P1=0`。

## Starting facts and preserved scope

- Starting HEAD：`62dd79a1f32c9f3cde5c7c4bd5c875df6ceaed78`；branch `dev`；开始时
  `HEAD == origin/dev`。
- Initial authority：schema 2；`PHASE_1 / IN_PROGRESS|NOT_FROZEN`；accepted batch
  `PHASE_1_MAINTENANCE_DEPENDENCIES / ACCEPTED|CI_GREEN`；current batch
  `PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE / NOT_STARTED`；next action
  `IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE`。
- P3 修复前保留 `37 modified / 16 untracked`；Git-ignored
  `.tools/phase1-2-backup/before-p3-fix.patch` 与 status snapshot 均保留。后续没有执行 reset、clean、
  checkout、rebase，也没有重建或替换既有 Phase 1.2 实现。
- Target：loopback wire contract、token、plugin listener/lifecycle、development Host、Python backend/doctor、
  确定性回归、安全脚本、authority regression 与本 attempt evidence。
- Excluded：Document/DocumentManager、Database、Editor、SelectionSet、ObjectId、Handle、DWG read/write、
  production command endpoint、AutoLISP、COM、write/script、multi-instance routing 与 Phase 1.3 实现。

## Previous blockers

- 初始主机 shell 无法解析 .NET SDK；恢复为 x64 .NET SDK `8.0.423`、MSBuild `17.11.48` 后继续，
  未跳过 .NET 验证。
- 原 Codex Security scan `9877cb53-4756-4151-93ee-f8f282303e42` 报告唯一
  `P3 / LOW`：`loopback-completed-task-retention`。
- AutoCAD UI demand-load 的个别早期协调尝试分别留下
  `PORT_CONFLICT_EVIDENCE_TIMEOUT` 或 `REAL_BRIDGE_LISTENER_TIMEOUT`；这些失败记录保留。后续在相同
  bundle 与代码上完成真实 status/lifecycle、恢复和 Bridge harness，不用超时结果冒充通过。
- AutoCAD 2026 初看未更新普通 `%LOCALAPPDATA%` evidence 文件。根因是从打包版 Codex 桌面控制启动
  AutoCAD 时，Windows 将该进程的 `LocalApplicationData` 重定向到 package `LocalCache/Local`；在该
  重定向后的 `CAD-MAX/evidence/plugin-lifecycle.jsonl` 中已核实全部 2026 记录，证据没有丢失。

## Transport, token, and capability boundary

- Production plugin：固定 `127.0.0.1:47770`；development Host：固定
  `127.0.0.1:47779`；不回退到 LAN、通配地址或随机端口。
- Token：机器本地 256-bit credential；当前用户 ACL；原子创建/替换；固定长度、常量时间比较；
  token、Authorization header、原始响应和路径不进入日志/evidence。
- Production endpoints 精确为 `GET /v1/health`、`/v1/version`、`/v1/capabilities`、
  `/v1/heartbeat`；GET-only、HTTP/1.1、无 body/query、bounded parser、bounded connections、bounded
  read/write timeout；不存在 `/v1/commands`。
- `instanceId` 每次 listener lifecycle 旋转；`capabilityRevision` 随能力状态更新；heartbeat sequence
  单调增长。`documentAccess=false / dwgRead=false / dwgWrite=false / allowWrite=false /
  allowScript=false`。
- Python `AutoCadBridgeBackend` 只调用上述 process-level endpoints；错误与 token 行为脱敏；实例断开或
  旋转时清空 capability cache；doctor 区分真实 AutoCAD 与 development Host。

## P3 root cause and fix

- Root cause：旧顺序先安装 completion continuation，再把 connection task 加入 active registry；若
  handler 同步完成，continuation 会先删除尚不存在的 entry，随后完成 task 被插入并滞留至 listener
  结束。
- Fix：`ActiveConnectionTaskRegistry.TryRegister` 先以单调且不复用的 `connectionId` 完成
  `TryAdd`，失败即 fail closed；然后用 `CancellationToken.None + ExecuteSynchronously +
  TaskScheduler.Default` 安装 cleanup；最后以 `task.IsCompleted` 做同步完成补偿。
- Cleanup 通过 task identity 与 `TryRemove` 保持幂等；同步 fault 的 aggregate exception 在内部观察并
  计数，不逃逸 accept loop；shutdown 先停止 accept owner，再用 `SnapshotRunning` 剔除已完成 task，
  不用 timer、scavenger、GC、sleep 或延长 timeout 掩盖竞态。

## Deterministic regression

- 同步成功：`Task.CompletedTask` 注册后 active count 与 shutdown snapshot 均为 `0`。
- 同步失败：`Task.FromException` 被移除，exception 被内部观察，listener 不崩溃。
- 同步取消：`Task.FromCanceled` 被移除，shutdown 正常。
- 异步完成：使用 `TaskCompletionSource(RunContinuationsAsynchronously)` 强制观察
  `active count 1 → 0`。
- Stress：10,000 次同步完成注册/清理，duplicate registration `0`、retained task `0`、cleanup
  exception `0`。
- Shutdown race：覆盖 snapshot 前完成、snapshot 后完成、并发/重复 stop、空 registry 与 running
  task；最终 `STOPPING → STOPPED`、active task count `0`、端口可立即重新绑定。

## Focused security review

- Focused scan：`2dcaeabb-6ed1-44ce-83b9-e4d71325b226`；validated working-tree diff；54/54
  surfaces complete；canonical findings `0`。
- 结论：`P0=0 / P1=0 / P2=0 / P3=0`；原
  `loopback-completed-task-retention=CLOSED`；未发现新的 task leak、deadlock 或 unobserved exception。
- Report SHA-256：
  `684043D5B81515B2E8D8A49349790BADE7C6E5A0EEB1C5DEFBCFE038BA274797`。
- 扫描限制：安全扫描本身未启动真实 AutoCAD、未读取 credential value、未运行代理或外部服务；
  真实 runtime/token/loopback 事实由下述独立本机验收提供，不以静态扫描推断。

## Local validation

- `git diff --check`、`uv sync --frozen`、Ruff lint/format、mypy、`cad-max-mcp doctor`：`PASS`。
- Python：`38/38 PASS`。
- locked .NET restore、Release build：`PASS`，`0 warnings / 0 errors`；tests：Contracts `3/3`、
  Plugin `50/50`、Bridge Core `13/13`，合计 `66/66 PASS`。
- PowerShell：AutoCAD safety `24/24`、token safety `10/10`、loopback Bridge `PASS`。
- `scripts/docs/verify-docs.ps1` 与最终 candidate 待提交快照的 `scripts/verify.ps1`：`PASS`；
  未执行的 exact-head CI 不在本节宣称通过。

## Real AutoCAD installation matrix

- AutoCAD 2025：检测到 `R25.0.58.0.0`；三项 managed SDK file version 均为 `25.0.58.0.0`；
  .NET 8 boundary `true`。
- AutoCAD 2026：检测到 `R25.1.60.0.0`；三项 managed SDK file version 均为 `25.1.60.0.0`；
  .NET 8 boundary `true`。
- 两版本 adapter/bundle build、manifest validation 均 `PASS`；app version `0.1.1`；Series 分别
  `R25.0 / R25.1`；bundle 内 Autodesk DLL `0`，构建和安装路径均未写入仓库。

## Real AutoCAD 2025 validation

- 当前用户 dev bundle 由 AutoCAD 官方 loader 加载；`CADMAXPLUGINSTATUS` 为 `READY`；listener 仅
  `127.0.0.1:47770`，owner 为当次 `acad` PID。
- 无 token/错 token 均 `401`；正确 token 的 health/version/capabilities/heartbeat 全部通过；4 个
  endpoint 使用同一 `instanceId`；heartbeat 单调；production commands endpoint 不存在；MCP 顶层工具
  数量未增加；bridge doctor `PASS`。
- 初始、restart、port-conflict recovery 与 cache-target 最终 harness 均通过。重启产生不同
  `instanceId`；Python 真实观察 disconnect、cache invalidation 与 4 项 capability reacquire。
- 每次正常关闭后 active connection task `0`、进程 `0`、47770 listener `0`，端口在 deadline 内释放。

## Real port-conflict validation

- 受控进程只占用 `127.0.0.1:47770`；AutoCAD 2025 plugin 未选随机端口，status 为 `DEGRADED`，
  lifecycle 记录 `PLUGIN_INITIALIZE_FAILED / PORT_IN_USE`，且没有 DWG/document 访问。
- 早期自动协调 timeout 记录保留；人工 status/lifecycle 确认上述 fail-closed 状态后正常关闭。停止 blocker
  后重启恢复 `READY`，真实 Bridge/doctor 通过，再次正常关闭并释放端口。

## Real AutoCAD 2026 validation

- 当前用户 dev bundle 真实加载；module hashes 与本轮 build/bundle 一致；status 为 `READY /
  autoCADYear=2026 / readOnly=true / allowWrite=false / allowScript=false`。
- 三个真实进程各自完整写入 6 个事件：`INITIALIZE_STARTED → LISTENER_STARTED →
  INITIALIZE_SUCCEEDED → TERMINATE_STARTED → LISTENER_STOPPED → TERMINATE_SUCCEEDED`；共 18 条，
  全部 `success=true`、无 safe error。package-local evidence 路径只在本机核验，原始 JSONL 未提交。
- 初始实例、restart 实例和 cache-target 实例的认证、4 endpoints、doctor、能力诚实性、shutdown port
  release 均 `PASS`；三次 `instanceId` 均不同。Python cache helper真实观察断开、失效和重新获取。
- 最终正常关闭后 AutoCAD process `0`、harness process `0`、listener `0`。

## Dependency vulnerability query and uninstall

- `dotnet list src/dotnet/CadMax.sln package --vulnerable --include-transitive`：成功；所有项目均无已知
  vulnerable package。
- 真实验收后按固定 ProductCode 精确卸载当前 2025/2026 dev bundle；active target `0`、staging target
  `0`。安装脚本此前生成的两个 `rollback-*` 备份原样保留，未删除其他 Autodesk plugin、token、
  lifecycle evidence 或 `.tools/phase1-2-backup`。

## Candidate and closeout

- Implementation commit：`UNCOMMITTED`；目标 message：
  `feat(bridge): add authenticated AutoCAD loopback lifecycle`。
- Candidate exact-head CI：`NOT_RUN`；必须验证 Governance、Python 3.12、.NET 8 全部 success，且
  run head SHA 精确等于 candidate。
- Closeout commit/run：`NOT_RUN`；只有 candidate CI GREEN 后才更新 authority，并以
  `docs(status): accept Phase 1.2 loopback bridge` 独立提交；closeout 自身还需 exact-head CI。
- Current authority 保持 Phase 1.2 `NOT_STARTED`、AutoCAD `NOT_CONNECTED`，未用本机 PASS 提前推进。

## Candidate CI recovery

- 初始 implementation candidate `c89626475a35f513d63089e4c0ddd3bc25d9176e` 已以
  `feat(bridge): add authenticated AutoCAD loopback lifecycle` 提交并推送；exact-head CI run
  `29764567086` 中 Python 3.12 success，Governance 与 .NET 8 failure，因此该 run 明确记为
  `COMMITTED|CI_FAILED|FIX_REQUIRED`，没有通过重跑冒充新 candidate。
- Governance 根因为 Windows Server 2025 runner 对新文件赋予隐式 Administrators owner；旧 token script
  先创建文件再收紧 DACL，没有显式建立 owner，因而 token safety fail closed。`.NET 8` 的三个 token loader
  测试与七个 Host endpoint 测试具有同一 fixture 根因，均返回 `TOKEN_FILE_INSECURE` 或在 Host build 前退出；
  Contracts `3/3` 与 Python job 不受影响。
- 恢复修复使用 `FileSystemAclExtensions.Create + FileMode.CreateNew`，在文件首次可见时一次性设置
  owner=current user、protected DACL、current user/SYSTEM FullControl；使用 `FileShare.None + WriteThrough +
  Flush(true)`，并清零 payload bytes。create/rotate 后显式验证 owner；生产 loader 的 owner/DACL fail-closed
  校验没有放宽。两个 .NET fixture 使用相同创建时安全描述符，避免依赖 runner 的隐式 owner。
- 修复后的 token safety `10/10`、AutoCAD script safety `24/24`、Plugin tests `50/50`、Bridge Core tests
  `13/13`、主工作树与独立 archived fresh fixture 的 `scripts/verify.ps1` 均 `PASS`，build
  `0 warnings / 0 errors`；NuGet vulnerability query 仍无已知漏洞。
- 对该 CI ACL remediation 另做人工 scoped security review：未发现 owner 降级、继承 DACL、覆盖现有 token、
  token/path 输出或生产校验绕过；`P0=0 / P1=0`。这不是新的 Codex Security scan，也不扩张 sealed focused
  scan `2dcaeabb-6ed1-44ce-83b9-e4d71325b226` 的覆盖声明。
- 修复 candidate 使用 `fix(bridge): create token files with explicit owner`；本恢复记录提交时 exact SHA/run
  尚未产生，必须由后续 candidate exact-head CI GREEN 记录补齐后才允许 authority closeout。

## Known limitations

- `autocad_runtime=CONNECTED` 在 closeout 后只表示 Python backend 通过认证 loopback HTTP 连接到真实
  AutoCAD 进程内 process-level endpoints；不表示任何 drawing/DWG/document 能力。
- DWG read/write 仍 `NOT_IMPLEMENTED`；write/script 仍 `DISABLED`；本 batch 不处理多实例路由。
- 用户态 loopback 不是抵御同用户恶意进程的独立 trust boundary；bearer token ACL 与 fail-closed
  capability boundary 仍必须保留。

## Rollback

- 未提交：只反向修改本 attempt target files；保留 `.tools/phase1-2-backup` 与用户已有改动；不使用
  reset/clean/checkout。
- 本机：保持 AutoCAD 关闭，核验固定 ProductCode 后精确卸载当前 bundle；必要时从已保留 rollback
  bundle 或 Git-ignored build bundle 恢复；不修改注册表、`SECURELOAD` 或 `TRUSTEDPATHS`。
- 已推送后：按逆序 `git revert <closeout-sha>`、`git revert <candidate-sha>`，不改写共享历史。
