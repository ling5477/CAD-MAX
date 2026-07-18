# CADMAX-DEPENDABOT-PR-CLEANUP-AND-DEPENDENCY-MAINTENANCE / attempt-01

Task classification：`CI_CD / DEPENDENCY_MAINTENANCE / DOCS_GOVERNANCE / SECURITY_BOUNDARY / TEST / GITHUB_PR_MAINTENANCE / GITHUB_PUBLISH`

Risk classification：`HIGH_RISK`

本 attempt 记录用户授权插入 Phase 1.1 与 Phase 1.2 之间的依赖维护。它不是 CAD 能力 work batch，
不实现 Phase 1.2，也不改变 AutoCAD、DWG、write/script 安全事实。后续 Group A/B/C、PR 关闭与
closeout 的已发生事实只追加到本文件，不用未来 SHA/run 替换本节的 `PENDING`/`NOT_RUN` 事实。

## 2026-07-18 / 起点与 authority reconciliation

- Repository：`https://github.com/ling5477/CAD-MAX.git`；branch：`dev`。
- Starting HEAD：`7d7c7fd0b5486eeaf90c87a64b4c2ac3e3a8fd9d`；preflight 时
  `HEAD == origin/dev` 且 worktree clean。
- Initial authority：accepted `PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP / ACCEPTED|CI_GREEN`，candidate
  `d149162938b239948048662c9db342c4a0b1fce3`，run `29641896446`；work batch
  `PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE / NOT_STARTED`；next action
  `IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE`。
- Reconciliation：machine contract 顺序在 Phase 1.1 与 Phase 1.2 之间最小插入
  `PHASE_1_MAINTENANCE_DEPENDENCIES`；current work batch 临时切换到 maintenance，Phase 1.2 未开始。
- Checker：新增 maintenance reconciliation、CI pending、maintenance accepted 后返回 Phase 1.2 正例；
  新增禁止跳过 maintenance 与禁止 pending 状态使用 `UNCOMMITTED` 的负例。
- Commit self-reference：`COMMITTED|CI_PENDING` 允许 `work_batch_commit=PENDING`；仅 pending 状态放宽，
  `COMMITTED|CI_GREEN|CONTINUE_REQUIRED` 与 `ACCEPTED|CI_GREEN` 仍要求 concrete 40-char SHA 和 run。

## 初始 Dependabot PR inventory

盘点命令为 `gh pr list` 与逐个 `gh pr view --json ...`。盘点时恰好 9 个 open PR，作者全部为
`app/dependabot`；没有其他作者或非 Dependabot open PR。

| PR | Dependency target | Head branch / SHA | Base SHA | Merge state | Changed files | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| #1 | `actions/setup-dotnet v4 -> v6` | `dependabot/github_actions/dev/actions/setup-dotnet-6` / `80743153fc601c208b812795d33f5f1d799a463c` | `3dfe150d079b4ecdda79dcd524a209989732b059` | `MERGEABLE / CLEAN` | `.github/workflows/ci.yml` | reapply on current `dev` |
| #2 | `actions/checkout v4 -> v7` | `dependabot/github_actions/dev/actions/checkout-7` / `e5d6f0a0898a40e6f0c8586dff7c5044f6ac443d` | `4bec3fa042e27c105e636504bae16c2f00ebd1e7` | `MERGEABLE / CLEAN` | `.github/workflows/ci.yml` | reapply on current `dev` |
| #3 | `astral-sh/setup-uv v6 -> v7` | `dependabot/github_actions/dev/astral-sh/setup-uv-7` / `8a7e34ad1012ee3060ee96dc57075dde28585c4d` | `3dfe150d079b4ecdda79dcd524a209989732b059` | `MERGEABLE / CLEAN` | `.github/workflows/ci.yml` | reapply on current `dev` |
| #4 | `actions/setup-python v5 -> v6` | `dependabot/github_actions/dev/actions/setup-python-6` / `c7b58e5684539734037d2420d242e5335480850e` | `3dfe150d079b4ecdda79dcd524a209989732b059` | `MERGEABLE / CLEAN` | `.github/workflows/ci.yml` | reapply on current `dev` |
| #5 | `pytest >=8.4,<9 -> >=8.4,<10` | `dependabot/pip/dev/pytest-gte-8.4-and-lt-10` / `1f678dda83adb7b14092a150fd616db0551ed98f` | `3dfe150d079b4ecdda79dcd524a209989732b059` | `MERGEABLE / UNSTABLE` | `pyproject.toml` | close; retain major ceiling |
| #6 | `mypy >=1.17,<2 -> >=1.17,<3` | `dependabot/pip/dev/mypy-gte-1.17-and-lt-3` / `727036d30bd23b7f9b89e6cfd7e615ff43fb7d73` | `3dfe150d079b4ecdda79dcd524a209989732b059` | `MERGEABLE / CLEAN` | `pyproject.toml` | close; retain major ceiling |
| #7 | `Microsoft.AspNetCore.Mvc.Testing 8.0.23 -> 8.0.29` | `dependabot/nuget/src/dotnet/CadMax.Bridge.Core.Tests/dev/Microsoft.AspNetCore.Mvc.Testing-8.0.29` / `f80b3237f1cc5dc3e661765a6c70993d62c0f706` | `3dfe150d079b4ecdda79dcd524a209989732b059` | `MERGEABLE / UNSTABLE` | Core Tests `.csproj` and lockfile | Group B |
| #8 | `Microsoft.NET.Test.Sdk 17.12.0 -> 18.8.1` | `dependabot/nuget/src/dotnet/CadMax.Bridge.Core.Tests/dev/multi-f1dad813ec` / `19184515c14020a53ded4fa09c2c959ec2fc682f` | `dfb11cb9acfed60cc4ef246f55018240ac5e8cb4` | `MERGEABLE / UNSTABLE` | Core Tests `.csproj` and lockfile | Group C, isolated |
| #9 | `xunit.runner.visualstudio 3.0.2 -> 3.1.5` | `dependabot/nuget/src/dotnet/CadMax.Bridge.Core.Tests/dev/multi-a929e63809` / `369f41a724ce1000d200a50ec7e00dd2f7fcc170` | `dfb11cb9acfed60cc4ef246f55018240ac5e8cb4` | `MERGEABLE / UNSTABLE` | Core Tests `.csproj` and lockfile | Group B |

## 升级前基线

- GitHub Actions：`actions/checkout@v4`（3 处）、`actions/setup-python@v5`、
  `astral-sh/setup-uv@v6`、`actions/setup-dotnet@v4`。
- Dependabot：3 个 weekly ecosystems 均 target `dev`；无 groups/ignore/auto-merge。
- Python boundaries：`pytest>=8.4,<9`、`mypy>=1.17,<2`；`uv sync --frozen` 后实际版本为
  `pytest 8.4.2`、`mypy 1.20.2`。
- .NET direct versions：`Microsoft.AspNetCore.Mvc.Testing 8.0.23`、
  `xunit.runner.visualstudio 3.0.2`、`Microsoft.NET.Test.Sdk 17.12.0`。
- Test projects：3；baseline discovery/result 为 Contracts `1/1`、Plugin `15/15`、Bridge Core `8/8`，
  合计 `24 passed / 0 skipped / 0 failed`。
- Baseline locked restore/build：成功，`0 warnings / 0 errors`；xUnit VSTest adapter `3.0.2`，未见
  adapter load 或 protocol negotiation 错误。
- 首次直接调用系统 `dotnet` 未发现 SDK；RCA 为系统安装无 SDK，按仓库 `scripts/verify.ps1` 的既有
  fallback 使用 `.tools/dotnet/dotnet.exe` 后上述基线通过，不是代码/依赖失败。

## Group A implementation candidate（成文时）

- Actions after：`actions/checkout@v7`（3 处）、`actions/setup-python@v6`、
  `astral-sh/setup-uv@v7`、`actions/setup-dotnet@v6`；保持 `fetch-depth: 0`、Python 3.12、.NET 8、
  cache、required jobs、`permissions: contents: read` 与 AutoCAD script safety gate。
- Dependabot policy after：GitHub Actions 全部分组；pytest/mypy 忽略 semver-major；仅将
  `Microsoft.AspNetCore.Mvc.Testing` 与 `xunit.runner.visualstudio` 分组；
  `Microsoft.NET.Test.Sdk` 保持独立。
- Group A commit/run/jobs：`PENDING / NOT_RUN`；不得据此关闭 #1–#4 或宣称 Node warning 消失。
- Group B commit/run/jobs：`NOT_RUN`。
- Group C commit/run/jobs：`NOT_RUN`。
- PR close results：尚未执行；open Dependabot PR count 仍为 9。
- Review：见下一节；Group A 已满足提交前 review gate。

## 2026-07-18 / Group A 本地验证与 high-risk review

- YAML：通过临时 `uv run --with pyyaml` 解析 `.github/workflows/ci.yml` 与
  `.github/dependabot.yml`，`PASS / YAML_SYNTAX_VALID files=2`；该临时工具未写入 `pyproject.toml` 或
  `uv.lock`。
- 首次 `scripts/verify.ps1`：Python 16 tests、doctor、18 个 AutoCAD script safety cases 已通过，随后
  authority checker 因正文写为 `COMMITTED / CI PENDING`、未精确包含 machine token 对应的
  `COMMITTED / CI_PENDING` 而 fail closed。RCA 后只修正该正文 token，未修改或放宽 checker。
- 修复后 `scripts/docs/verify-docs.ps1`：authority regression/current authority、62 个 Markdown links
  全部通过，0 warning / 0 error。
- 修复后 `scripts/verify.ps1`：PASS；Ruff、format、mypy、Python `16/16`、doctor、安全脚本 `18/18`、
  docs governance、locked .NET restore/build/test 全部成功；.NET build `0 warnings / 0 errors`，测试
  Contracts `1/1`、Plugin `15/15`、Bridge Core `8/8`，合计 `24/24`。
- Diff audit：workflow 只有 6 个 `uses:` 版本替换；checkout v7 精确 3 处，其余目标 Action 各 1 处；
  无旧 Action 引用、无 `continue-on-error`、无 permissions/required jobs/safety gate 改动；
  `pyproject.toml` 与 `uv.lock` 无 diff；变更文件名未命中凭证、Autodesk/DWG/二进制禁入项。
- CodeRabbit CLI `0.6.5`（agent auth 有效）审查全部 8 个 Group A 文件，raised 1 minor issue：建议将
  NuGet 组限制为 `update-types: patch`。该建议不适用，因为已授权目标
  `xunit.runner.visualstudio 3.0.2 -> 3.1.5` 是 semver-minor；采用建议会让 runner 脱离指定维护组。
  当前组仅包含两个精确 package patterns，不扩大到其他 NuGet 包。
- Review result：`P0=0 / P1=0 / REVIEW_ACCEPTED|READY_TO_COMMIT`；上述 1 个 minor 建议经核验拒绝，
  没有需要修复的 reportable issue。
- Node runtime annotations：本地无法验证；必须以 Group A exact-head GitHub Actions annotations 为准，
  当前不得宣称 warning 已消失。

## 固定安全事实与限制

- `autocad_runtime=NOT_CONNECTED`、`dwg_read=NOT_IMPLEMENTED`、`dwg_write=NOT_IMPLEMENTED`、
  `read_only=ENABLED`、`allow_write=DISABLED`、`allow_script=DISABLED`、
  `http_binding=LOOPBACK_ONLY`。
- 未实现/启动 Phase 1.2，未访问 AutoCAD、DWG、Autodesk SDK/DLL 或真实生产接口。
- Node runtime annotations、Group A/B/C exact-head CI、PR 关闭、final candidate 与 closeout 均待后续
  真实执行后追加；当前已知限制不得写成 PASS。
- Rollback：已推送后按组使用 `git revert <Group C>`、`git revert <Group B>`、
  `git revert <Group A>`；authority closeout 单独 revert。禁止 reset/force push。

## 2026-07-18 / Group A exact-head CI 与 PR #1–#6 cleanup

- Group A commit：`41950edefe523ccfb9ea03cb3a91ae1ba0326dbf`；push 后
  `HEAD == origin/dev`，worktree clean。
- Group A CI run：`29644342972`，`headSha` 与 Group A commit 精确一致，`completed / success`。
- Group A jobs：Governance `success`（job `88079940700`）、Python 3.12 `success`
  （job `88079940729`）、.NET 8 `success`（job `88079940705`）。
- Node runtime annotations：逐个读取上述 check-run annotations，三个 job 均为空；本任务升级的
  `checkout@v7`、`setup-python@v6`、`setup-uv@v7`、`setup-dotnet@v6` 未产生 Node.js 20 deprecation，
  也没有其他未消除 annotation。
- PR #5/#6：确认 `pytest>=8.4,<9` 与 `mypy>=1.17,<2` 不变后，分别用保留 major ceiling 的说明
  显式关闭。
- PR #1–#4：Group A push 后由 Dependabot 自动识别 current `dev` 已覆盖并关闭；随后每个 PR 均补充
  包含 Group A commit 与 run `29644342972` 的审计说明，核验每个 PR 恰有一条匹配说明。
- PR close state：#1、#2、#3、#4、#5、#6 均为 `CLOSED`；#7、#8、#9 此时仍 open。
- Authority：Group A exact-head green 后记录为
  `COMMITTED|CI_GREEN|CONTINUE_REQUIRED`；maintenance 未 accepted，Phase 1.2 未开始。

## 2026-07-18 / Group B .NET patch 与 runner 本地验证

- Direct reference scope：3 个 test projects 均直接引用 `xunit.runner.visualstudio`，全部从 `3.0.2`
  更新到 `3.1.5`；只有 `CadMax.Bridge.Core.Tests` 直接引用
  `Microsoft.AspNetCore.Mvc.Testing`，从 `8.0.23` 更新到 `8.0.29`。
- `Microsoft.NET.Test.Sdk` 在三个 test projects 中均保持 `17.12.0`；xUnit、生产依赖、target framework、
  .NET SDK 与业务代码未变。
- 受控 restore：`dotnet restore ... --force-evaluate` 成功；随后 `--locked-mode` restore 成功。restore
  未输出 warning。机械 EOF 变更已从无关 lockfile 消除，语义 lock diff 仅涉及上述直接依赖与
  `Microsoft.AspNetCore.TestHost 8.0.29` 对应传递更新。
- Release build：成功，`0 warnings / 0 errors`。
- Test discovery/result：Contracts `1/1`、Plugin `15/15`、Bridge Core `8/8`，合计
  `24 passed / 0 skipped / 0 failed`，与 baseline 一致，没有无解释减少。
- Adapter：三个 test projects 均加载 `xUnit.net VSTest Adapter v3.1.5`；未见 adapter load、testhost
  或 protocol negotiation 错误。
- Group B commit/run/jobs：`PENDING / NOT_RUN`；#7/#9 尚不得关闭。
