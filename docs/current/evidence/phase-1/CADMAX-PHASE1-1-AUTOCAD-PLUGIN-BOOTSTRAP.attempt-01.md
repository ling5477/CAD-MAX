# CADMAX-PHASE1-1-AUTOCAD-PLUGIN-BOOTSTRAP / attempt-01

## Task classification

`CODE_CHANGE / AUTOCAD_PLUGIN / SDK_INTEGRATION / SECURITY_BOUNDARY / TEST / CI_CD / GITHUB_PUBLISH`

Risk classification：`HIGH_RISK`；涉及 Autodesk 专有 SDK、本机 AutoCAD 进程内加载、当前用户
`.bundle` 安装与 work-batch authority 推进，提交前必须独立 review 且 `P0=0 / P1=0`。

## Starting facts

- Starting HEAD：`127639577a205d2d4acb239a812129d54813a6e6`；branch `dev`；开始时
  `HEAD == origin/dev` 且 worktree clean。
- Initial authority：schema 2；`PHASE_1 / IN_PROGRESS|NOT_FROZEN`；accepted batch
  `PHASE_1_PLAN / ACCEPTED|CI_GREEN`；current batch
  `PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP / NOT_STARTED`；next action
  `IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP`。
- 固定安全事实：AutoCAD `NOT_CONNECTED`；DWG read/write `NOT_IMPLEMENTED`；read-only `ENABLED`；
  write/script `DISABLED`；HTTP `LOOPBACK_ONLY`。

## Detected AutoCAD versions

- AutoCAD 2025：检测到获许可本机安装；product version `R25.0.58.0.0`；`AcMgd.dll`、
  `AcDbMgd.dll`、`AcCoreMgd.dll` 文件版本均为 `25.0.58.0.0`；`.NET 8` boundary 满足；路径脱敏。
- AutoCAD 2026：未安装，当前与最终矩阵必须保持 `NOT_RUN`，不得推断通过。

## Target and excluded files

- Target：SDK-free plugin core/tests、独立 SDK-bound adapter、`packaging/autocad` template、
  `scripts/autocad`、默认验证/CI 安全负例、SDK setup、Phase 1 attempt evidence 和允许追加的 ledger。
- Excluded：`src/python/cad_max_mcp` 能力、MCP tool list、Bridge endpoint、Phase 1.2+、DWG/document
  handler、HTTP/listener、COM、AutoLISP、write/script、`main` 与其他仓库。

## SDK reference strategy

- 本机路径只写入 Git-ignored `.tools/autocad/CadMax.AutoCAD.Local.props`；提交的 example 为空占位。
- adapter 显式引用三项 Autodesk assemblies，全部 `Private=false`；SDK root 必须在仓库与 bundle staging
  外，并匹配本机检测到的目标年份安装。
- adapter 不进入 `CadMax.sln`；GitHub-hosted 默认 CI 不提供也不需要 Autodesk DLL；缺配置 fail closed。

## Bundle strategy

- 提交固定 identity 的 `PackageContents.xml.template`；按年份生成独立
  `.tools/autocad/bundles/CAD-MAX-<year>.bundle`。
- AutoCAD 2025 Series=`R25.0`；AutoCAD 2026 Series=`R25.1`；来源为本机 product release 与 Autodesk
  官方 PackageContents/RuntimeRequirements 文档。本 attempt 不冻结共用双版本 bundle。
- 当前用户安装采用 staging、完整校验、精确 identity 与 rollback 副本；卸载不接受路径/通配符输入，
  只删除固定名称且 ProductCode 匹配的 CAD-MAX bundle。

## Local implementation evidence

- SDK-free tests：lifecycle 合法/非法转换、初始化失败、host-process attestation、幂等、status allowlist、
  安全默认值、evidence 脱敏/有界 append/IO 隔离共 `15/15 PASS`；默认 solution 总计 `.NET 24/24
  PASS`，0 warning / 0 error。
- PowerShell safety tests：`18/18 PASS`；覆盖 SDK fail-closed、有效/无效 manifest、重复组件/命令、
  非 allowlist 文件、Autodesk binary、路径/UNC 输出脱敏及 cleanup fail-closed；fixture 位于
  Git-ignored 路径并已清理，输出路径为 `REDACTED`。
- 统一验证：`scripts/verify.ps1 PASS`；Python `16/16 PASS`，ruff/mypy/doctor PASS，文档治理与链接
  PASS，固定默认值为 `readOnly=true / allowWrite=false / allowScript=false / httpHost=127.0.0.1`。
- SDK-bound build：AutoCAD 2025 `PASS`；adapter version `0.1.0.0`；warnings-as-errors；输出位于
  Git-ignored path；构建输出 Autodesk DLL 数量 `0`。
- Bundle validation：AutoCAD 2025 `PASS`；app version `0.1.0`；Series=`R25.0`；相对 ModuleName；
  manifest valid；bundle Autodesk DLL 数量 `0`。
- AutoCAD 2026 SDK build/bundle runtime matrix：`NOT_RUN`（未安装）。

## Real load evidence

- AutoCAD 2025 real load：`PASS`；在无 `acad` 进程时安装当前用户开发 bundle，由 AutoCAD 官方
  bundle loader 识别；未签名程序集提示由用户仅选择“加载一次”，未修改 `SECURELOAD`、
  `TRUSTEDPATHS` 或注册表。加载模块来自已安装 CAD-MAX bundle，adapter/plugin SHA-256 与本轮生成
  bundle 完全一致。
- `CADMAXPLUGINSTATUS`：真实执行 `PASS`；返回 plugin `CAD-MAX`、version `0.1.0`、schema `1.0`、
  lifecycle `READY`、AutoCAD year `2025`、`readOnly=true`、`allowWrite=false`、`allowScript=false`。
- Initialize evidence：`PASS`；最终进程依次写入 `PLUGIN_INITIALIZE_STARTED`（`STOPPED→STARTING`）
  与 `PLUGIN_INITIALIZE_SUCCEEDED`（`STARTING→READY`），均 `success=true`。
- Terminate/normal shutdown evidence：`PASS`；正常关闭 AutoCAD 后进程数为 0，并依次写入
  `PLUGIN_TERMINATE_STARTED`（`READY→STOPPING`）与 `PLUGIN_TERMINATE_SUCCEEDED`
  （`STOPPING→STOPPED`），均 `success=true`。
- 最终四事件序列 SHA-256：
  `b64e55f3d13d3e85b54cc9d9a91d1c285f5c0352b760965949f8f2b4c2f6182d`；完整本机文件
  SHA-256：`2b3cc5da7a5e176f078de459a1d07fd5885b63fde2a606cfdfb38c3c5e8f7418`；路径脱敏，
  property allowlist 与隐私扫描 finding 均为 0，原始 JSONL 未复制进仓库。
- AutoCAD 关闭后已按固定 ProductCode 精确卸载当前用户 CAD-MAX 2025 bundle；未删除 lifecycle
  evidence、SDK props 或其他插件。

## Security and review

- Autodesk binary scan：最终对 `git ls-files -co --exclude-standard` 覆盖的 tracked/worktree 文件扫描为
  `PASS`；Autodesk managed DLL `0`、任意 DLL `0`、packaging/bundle DLL `0`、SDK/binary archive
  `0`、generated bundle payload `0`。本机 build 与已验证 bundle 中 Autodesk DLL 也均为 `0`。
- Sensitive path/secret scan：`PASS`；本轮 candidate 文件中当前用户名、真实机器标识、本机 Autodesk
  路径与 probable secret assignment 均为 `0`。仓库扫描中出现的绝对路径文本仅为既有文档占位、脱敏
  检测表达式和 PowerShell 安全负例的 synthetic sentinel；逐项复核未包含真实本机路径。`.env`、registry
  export、machine-local props、generated `PackageContents.xml`、raw lifecycle/debug log 均为 `0`；
  `git diff --check` 通过。原始 lifecycle JSONL 未进入仓库。
- Independent review：Codex Security diff scan reportable finding `0`；CodeRabbit 对 `.github`、
  `packaging`、`docs`、plugin core、SDK adapter、plugin tests 与 `scripts` 完成 scoped review。
  早期 Major/Minor 已逐项修复并重跑，最终所有 scope 均明确返回 `review_completed / findings=0`；
  接受结论 `P0=0 / P1=0 / REVIEW_ACCEPTED|READY_TO_COMMIT`。
- DWG access：无实现；network listener：无新增；write/script 固定关闭；MCP capability 无变化。

## Candidate and closeout

- Candidate commit：`UNCOMMITTED`。
- Candidate CI run/jobs：`NOT_RUN`。
- Closeout commit：`NOT_RUN`。
- Closeout CI run/jobs：`NOT_RUN`。
- Current authority 保持 Phase 1.1 `NOT_STARTED`；`STATUS.md` 与 current `ROADMAP.md` 尚未修改。

## Known limitations

- AutoCAD 2026 未安装，必须保持 `NOT_RUN`；双版本完整 load matrix 留待后续真实 integration。
- AutoCAD 2025 本轮只证明 official bundle load、status command 与进程 Initialize/Terminate；未建立
  Python MCP → loopback Bridge → AutoCAD 连接，因此 `autocad_runtime` 仍必须为 `NOT_CONNECTED`。
- 编译、bundle validation 与 CI 均不替代上述真实 AutoCAD evidence；本证据也不代表 DWG read/write、
  listener、write/script 或双版本共用 bundle 已实现。

## Rollback

- 未提交：只撤销本 attempt 的 target files，并保留用户已有改动；不 reset/clean。
- 本机安装：正常关闭 AutoCAD，精确卸载当前年份 CAD-MAX bundle，必要时核验 identity 后恢复 rollback
  副本；不修改注册表、`SECURELOAD` 或 `TRUSTEDPATHS`。
- 已推送后：按逆序 `git revert <closeout-sha>`、`git revert <candidate-sha>`，不改写共享历史。
