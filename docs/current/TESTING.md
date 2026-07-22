# 验证证据账本

本文件为 append-only evidence ledger。每条记录必须包含日期、commit/工作区状态、真实执行命令、结果和未验证项；失败记录不得删除或改写为通过。

## 2026-07-17 / Bootstrap CI remediation baseline

- 基线 commit：`3dfe150d079b4ecdda79dcd524a209989732b059`。
- GitHub Actions run `29573016709`：Python 3.12 job 全部成功；.NET 8 Build 失败，Test 未运行。
- `uv run pytest --basetemp .tools/pytest-temp`：16 passed；该参数规避当前 Windows 用户默认 TEMP 目录的权限错误，不改变测试集合。
- 修复后 `dotnet build ... --configuration Release --no-restore`：0 warnings / 0 errors。
- 修复后 `dotnet test ... --configuration Release --no-build`：Contracts 1 passed；Bridge/Core/Host 8 passed。
- 统一 `scripts/verify.ps1`、documentation governance、transport smoke 与修复提交 exact-head CI：本记录创建时尚未完成，不得据此宣称通过。

## 2026-07-17 / Pre-commit full local validation

- 工作区状态：修复与治理变更尚未提交。
- `scripts/verify.ps1`：PASS；`uv sync --frozen`、Ruff lint/format、mypy、16 个 Python tests、doctor、documentation governance、locked .NET restore/build/test 全部成功。
- .NET build：0 warnings / 0 errors；.NET tests：Contracts 1 passed，Bridge/Core/Host 8 passed。
- Documentation governance：authority 正/负例 PASS；current authority PASS；34 个 Markdown 相对链接、0 warning、0 error。
- stdio MCP：通过 SDK 启动 `uv run cad-max-mcp serve --transport stdio`，initialize 与 list_tools PASS；工具精确为 `cad_system`、`drawing`。
- Streamable HTTP MCP：`127.0.0.1:47771/mcp` initialize 与 list_tools PASS；工具精确为 `cad_system`、`drawing`；测试 PID 树已停止且端口已释放。
- Workflow YAML：本地解析 PASS，实际 jobs 为 `governance`、`python`、`dotnet`。
- 提交内容卫生：`git diff --check` PASS；tracked 文件名未发现 `.env`、密钥、Autodesk/构建二进制或 `.tools/.venv/bin/obj`。
- 修复提交与 governance 提交的 GitHub Actions：`NOT RUN`（尚未推送）；不得据此宣称 CI_GREEN。

## 2026-07-17 / Phase 0 implementation candidate CI

- Candidate HEAD：`4bec3fa042e27c105e636504bae16c2f00ebd1e7`，且当时 `HEAD == origin/dev`。
- GitHub Actions run：`29578296421`，`completed / success`。
- Jobs：Governance success；Python 3.12 success；.NET 8 success。
- 该 run 接受 Phase 0 实现候选；当前 docs-only closeout commit 在创建时尚未 push，其 exact-head CI 必须另行验证。

## 2026-07-17 / Phase 1 plan candidate 与 schema 2 closeout local validation

- Phase 1 plan/governance candidate：`f67eed18aa2658ab05c8aa4d8bd0ed9b8fcbe120`；当时
  `HEAD == origin/dev`。
- Candidate GitHub Actions run `29592363444`：`completed / success`；Governance、Python 3.12、
  .NET 8 全部成功，head SHA 与 candidate 精确一致。
- Schema 2 closeout worktree 的 `scripts/verify.ps1`：PASS；Ruff、mypy、16 个 Python tests、doctor、
  contract/authority regression、60 个 Markdown relative links、locked .NET restore/build/test 全部成功。
- .NET build：0 warnings / 0 errors；.NET tests：Contracts 1 passed，Bridge/Core/Host 8 passed。
- Authority checker：`PHASE_1 / IN_PROGRESS|NOT_FROZEN`；`PHASE_1_PLAN / ACCEPTED|CI_GREEN`；
  `PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP / NOT_STARTED`；下一动作匹配。
- AutoCAD 2025/2026 plugin、Autodesk SDK、真实 DWG read/write：`NOT_RUN / NOT_IMPLEMENTED`。
- Closeout commit 与 exact-head GitHub Actions：本条创建时为 `NOT_RUN`，不得以 candidate run 代替。

## 2026-07-17 / Phase 1 authority closeout exact-head CI

- Authority closeout HEAD：`92aaa83376eebeb47b9f02fae269cd2d02822105`，且当时
  `HEAD == origin/dev`。
- GitHub Actions run `29592807681`：`completed / success`；head SHA 与 closeout HEAD 精确一致。
- Jobs：Governance success；Python 3.12 success；.NET 8 success。
- 该 run 独立验证 schema 2 `STATUS.md`、current ROADMAP、append-only ledger 与 candidate evidence；
  未使用 candidate run `29592363444` 替代 closeout run。
- GitHub Actions 对现有 JavaScript action 输出 Node.js 20 deprecation annotation；required jobs 未失败，
  workflow 升级不属于本 work batch，后续单独评估。
- 本条与 attempt-01 对 closeout SHA/run 的追记将形成 evidence attestation commit；该 commit 仍需自身
  exact-head CI，不能把本 run 当作 attestation run。

## 2026-07-18 / Phase 1.1 plugin bootstrap candidate acceptance

- Implementation commit `dfb11cb9acfed60cc4ef246f55018240ac5e8cb4` 已 push；首次 exact-head CI run
  `29640778847` 中 Python 3.12 与 .NET 8 成功，Governance 的 AutoCAD script safety step 失败。
- RCA：18 个 PowerShell safety assertions 实际全部通过，但最后一个预期失败子进程留下
  `$LASTEXITCODE=1`，脚本在 GitHub Actions 直接调用时继承该退出码。最小修复 commit
  `d149162938b239948048662c9db342c4a0b1fce3` 在统一失败闸门与 PASS 输出后显式 `exit 0`；未删除、
  跳过或放宽测试。
- 最终 candidate `d149162938b239948048662c9db342c4a0b1fce3` 的 exact-head CI run
  `29641896446` 为 `completed / success`；Governance、Python 3.12、.NET 8 全部成功。
- `scripts/verify.ps1`：PASS；Python `16/16`、默认 .NET `24/24`（其中 plugin tests `15/15`）、
  PowerShell safety `18/18`，build 0 warning / 0 error，docs/authority 与固定安全默认值全部通过。
- AutoCAD 2025 SDK-bound adapter build、bundle validation 与真实加载均为 `PASS`；
  `CADMAXPLUGINSTATUS` 返回 `READY`，且 `readOnly=true / allowWrite=false / allowScript=false`；正常退出后
  Initialize/Terminate 四事件完整。AutoCAD 2026 未安装，保持 `NOT_RUN`。
- 原完整 diff Codex Security 与 CodeRabbit 最终 scoped reviews 均为 reportable finding `0`；CI 单行修复的
  focused Codex Security scan `f536c81c-36c2-4da3-8dba-fc569d3107fc` 亦为 finding `0`。CodeRabbit CLI
  新实例在 OAuth 后无法取得 user data，因此不得宣称单行修复获得新的 CodeRabbit review。
- 本条所在 docs-only closeout commit 的 SHA 在成文时不可自引用，且其 exact-head CI 尚未运行；结果只在
  最终报告中记录，不创建第三个纯 attestation commit。

## 2026-07-18 / Dependency maintenance Group B/C candidate validation

- Group B candidate `c99f855212ed96cad751b0807da34a54deea362a` 的 exact-head GitHub Actions run
  `29646011345` 为 `completed / success`；Governance job `88084275412`、Python 3.12 job
  `88084275458`、.NET 8 job `88084275422` 全部成功。
- Group C 将三个 test project 的 `Microsoft.NET.Test.Sdk 17.12.0` 独立升级到 `18.8.1`；
  force-evaluate restore 与 locked restore 均成功，Release build 为 `0 warnings / 0 errors`。
- Group C 本地测试发现与结果为 Contracts `1/1`、Plugin `15/15`、Bridge Core `8/8`，合计
  `24 passed / 0 failed / 0 notExecuted`；三份 TRX 未发现 adapter、testhost 或 protocol negotiation
  错误，临时 TRX 已删除。
- Group C 工作区的 `scripts/verify.ps1`：PASS；Python 实际解析版本为 `pytest 8.4.2`、
  `mypy 1.20.2`，声明边界仍为 `pytest>=8.4,<9`、`mypy>=1.17,<2`。
- Group C candidate `ba69004657adaef217ed12dd99adbb07ec1e2be9` 的 exact-head GitHub Actions run
  `29647121269` 为 `completed / success`；Governance job `88087116999`、Python 3.12 job
  `88087116985`、.NET 8 job `88087116982` 全部成功。
- 本条所在 dependency maintenance closeout commit 在成文时尚未创建；不得以 Group C run
  `29647121269` 代替 closeout exact-head CI。
- Closeout 五文件工作区首次执行 `scripts/docs/verify-docs.ps1` 与 `scripts/verify.ps1` 均为 PASS：
  authority regression/current authority、63 个 Markdown links、Ruff、mypy、Python `16/16`、doctor、
  safety `18/18`、locked .NET restore/build/test 全部成功；.NET build `0 warnings / 0 errors`，测试
  Contracts `1/1`、Plugin `15/15`、Bridge Core `8/8`。本条追加后仍需重跑验证覆盖最终待提交快照。

## 2026-07-21 / Phase 1.2 authenticated loopback Bridge candidate acceptance

- 初始 candidate `c89626475a35f513d63089e4c0ddd3bc25d9176e` 的 exact-head CI run
  `29764567086` 中 Python 3.12 success，Governance 与 .NET 8 failure；失败历史保留。RCA 为 Windows
  Server 2025 runner 的隐式 Administrators file owner，使 token script 与两个 .NET fixtures 按生产策略
  返回 `TOKEN_FILE_INSECURE`。
- 创建时显式 owner/protected DACL 的最小修复 candidate
  `031ea139e73ffeacb4a5b717a8051afc9b7d287c` 已 push；exact-head CI run `29766557136` 为
  `completed / success`。Jobs：Python 3.12 `88434097097`、Governance `88434097105`、.NET 8
  `88434097108` 全部 success。
- 最终工作树与独立 archived fresh fixture 的 `scripts/verify.ps1` 均 PASS：Ruff、format、mypy、
  Python `38/38`、doctor、AutoCAD safety `24/24`、docs governance、locked restore、.NET
  Contracts `3/3`、Plugin `50/50`、Bridge Core `13/13`；Release build `0 warnings / 0 errors`。
- Token safety `10/10`、development loopback Bridge、端口释放均 PASS；NuGet direct/transitive
  vulnerability query 成功且无已知漏洞。
- Focused Codex Security scan `2dcaeabb-6ed1-44ce-83b9-e4d71325b226` 完成 `54/54` surfaces，
  findings `0`；原 `loopback-completed-task-retention` 为 CLOSED，`P0=0 / P1=0 / P2=0 / P3=0`。
- 真实 AutoCAD 2025/2026 均完成认证四 endpoint、doctor、shutdown port release、restart instanceId
  rotation 与 Python capability cache reconnect；AutoCAD 2025 端口冲突 fail-closed/recovery PASS，最终
  active task/process/listener count 均为 `0`。当前 dev bundle 已精确卸载，rollback bundle 保留。
- 本条所在 docs-only closeout commit 在成文时尚未创建；其 exact-head CI 必须独立全绿，不得以
  candidate run `29766557136` 替代。

## 2026-07-22 / Phase 1.3 document context dispatch pre-candidate validation

- 工作区基线 `3d0f20b8c00b789968a44f129834de3122a15cf6`；authority 为 Phase 1.3
  `NOT_STARTED`、next action `IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH`，未提前推进状态。
- Focused validation：Python `17/17`；.NET Contracts `5/5`、Plugin `66/66`、Bridge Core `13/13`，
  合计 `84/84`；AutoCAD PowerShell safety `25/25`；`git diff --check` 全部 PASS。
- 真实 AutoCAD 2025/2026 均完成 main-thread + official document command-context、stable/distinct opaque
  routing、wrong-active、destroy、zero-document、modal、busy、timeout、disconnect cancellation、queue
  saturation、shutdown/port release、restart instance rotation 与 old-document-ID invalidation；最终 process、
  queue、in-flight、listener 均为 `0`。
- Standard security scan `779c3ee1-52b5-41f6-a907-a54a31d7e75f`：review receipts `51/51`；
  `P0=0 / P1=0 / P2=0 / P3=2`。两个 P3 为既有 bearer token ACL control-right 与 peer-identity 限制，
  已有 write-up/PoC，并保留 read-only/loopback-only/write-script-disabled 限制。
- Final `scripts/verify.ps1` PASS：Python `41/41`、.NET `84/84`、AutoCAD safety `25/25`、Release build
  `0 warning / 0 error`，Ruff/format/mypy/doctor/docs 全部通过。Implementation commit/push 与 exact-head CI
  在本条写入时尚未执行；不得以本地 PASS 代替 candidate CI。

## 2026-07-22 / Phase 1.3 implementation candidate acceptance

- Candidate `8dc5d25630c681b1070b06443a80ca43a269c7bd` 已 push，且当时
  `HEAD == origin/dev`；exact-head CI run `29934893298` 为 `completed / success`。
- Jobs：Python 3.12 `88973799136`、Governance `88973799176`、.NET 8 `88973799172` 全部 success。
- Run 汇总曾在 3/3 jobs success 后短暂滞留 `in_progress`，普通/force cancel API 返回 HTTP 500；同 SHA
  recovery run `29935780959` 被发起。原 push run 随后正常 success，recovery run 已提交 cancel，不用于
  acceptance。
- 本条所在 docs-only closeout commit 在成文时尚未创建；其 exact-head CI 必须独立全绿，不得以
  candidate run `29934893298` 代替。
