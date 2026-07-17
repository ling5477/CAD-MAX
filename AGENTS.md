# AGENTS（CAD-MAX 开发规范）

> 目的：让开发者与编程代理在 CAD-MAX 中遵循同一事实源、安全边界、模块边界、验证和 Git/CI 纪律。

## 1. 当前事实源

- 每轮任务先读取 `docs/current/STATUS.md` 顶部的 `cad-max-current-authority` 机器可读区块。
- `STATUS.md` 是当前 Phase、下一动作和安全能力状态的唯一 authority。
- `docs/current/ROADMAP.md` 只定义下一允许动作，不覆盖 `STATUS.md`。
- `README.md` 与 `docs/current/README.md` 只提供入口，不复制独立的阶段判定。
- `docs/current/TESTING.md` 与 `docs/current/WORKLOG.md` 是 append-only evidence ledger，不参与当前阶段判定。
- `docs/current/PHASE_1_AUTOCAD_CONNECTION_PLAN.md` 定义 Phase 1 work-batch 顺序，但不推进 Phase。
- `scripts/docs/governance-workflow-contract.json` 是 authority schema、work-batch lifecycle、evidence 命名和 hard blocker 的机器合同；checker 必须读取它，不得复制状态列表。
- `docs/current/evidence/phase-1/**` 保存不可覆盖 attempt evidence，不决定当前 Phase。
- current 文档互相冲突时，停止写操作并报告 `BLOCKED / CURRENT_AUTHORITY_CONFLICT`。

## 2. 任务边界

开始前必须明确 repository、target files、excluded files、风险和验收标准，并执行：

```powershell
Get-Location
git status --short
git branch --show-current
```

默认规则：

- 最小变更，不顺手重构、格式化或升级依赖。
- 不覆盖用户已有改动。
- review-only / audit-only 默认不修改文件。
- 任务开始时必须明确 target files、excluded files、docs budget、expected output 与 rollback boundary。
- 未执行的验证不得写成通过。
- 发现额外问题只记录；仅当其阻塞当前验收时做最小修复。

## 3. 模块边界

- `src/python/cad_max_mcp`：MCP transport、配置、后端选择、客户端安全响应。
- `contracts`：语言无关 JSON Schema，不依赖 Python、.NET 或 Autodesk 类型。
- `CadMax.Contracts`：C# wire contract，必须与 JSON/Python camelCase 语义一致。
- `CadMax.Bridge.Core`：校验、注册、调度、超时、取消和结构化异常映射。
- `CadMax.Bridge.Host`：仅限 localhost 的开发/测试 Host。
- `CadMax.AutoCAD.Plugin`：未来 Autodesk Managed .NET API 边界；不得提交 Autodesk DLL。
- Python 不得以 COM 绕过 C# Bridge 作为生产主路径。

## 4. 强制安全边界

- 默认保持 `readOnly=true`、`allowWrite=false`、`allowScript=false`、`httpHost=127.0.0.1`。
- Phase 0 不实现真实 AutoCAD 连接、DWG 读取或 DWG 修改。
- 未实现能力必须 fail closed，不能注册成功 stub 或伪造 drawing 数据。
- 不读取、提交或输出 `.env`、token、cookie、密钥、Autodesk 专有 DLL、SDK、许可证数据或完整本机路径。
- 外部 HTTP 必须有超时、错误映射和 loopback 限制；日志不得包含原始异常消息或响应体。
- 未来写操作必须另行通过权限、allowlist、document context、transaction、Undo、幂等和失败测试审查。

## 5. 文档纪律

- Code-first：普通代码任务默认不改文档；事实、入口或操作方式变化时才同步。
- Docs budget：
  - 普通代码任务默认不改文档；确需记录时最多向 `WORKLOG.md` 追加一条事实记录。
  - 测试基线变化可追加 `TESTING.md` 与 `WORKLOG.md`。
  - Phase 状态变化才允许修改 `STATUS.md` 与 current `ROADMAP.md`。
  - `README.md` 只在入口、架构、启动方式或阶段总状态变化时修改。
  - CI、安全、协议、Autodesk SDK、发布等高风险事项才新建专项计划。
- current 治理文档以中文为主；代码符号、命令、协议字段和状态 token 保留英文。
- ledger 只追加新证据，不重写历史失败；历史失败必须保留后续修复/重跑关联。
- 修改文档后必须运行 `scripts/docs/verify-docs.ps1`。

## 6. 验证纪律

统一验证：

```powershell
.\scripts\verify.ps1
```

最低分层验证：

```powershell
uv sync --frozen
uv run ruff check .
uv run ruff format --check .
uv run mypy src/python
uv run pytest
uv run cad-max-mcp doctor

dotnet restore src/dotnet/CadMax.sln --locked-mode --configfile NuGet.Config
dotnet build src/dotnet/CadMax.sln --configuration Release --no-restore
dotnet test src/dotnet/CadMax.sln --configuration Release --no-build

.\scripts\docs\verify-docs.ps1
```

修复 bug 必须有回归验证。外部服务或真实 AutoCAD 未运行时，必须明确写为未验证。

## 7. Git 与 CI

- `dev` 是默认集成和日常开发分支；`main` 是稳定发布线。
- 使用 Conventional Commits；功能、修复、治理文档和格式化不得混为一个提交。
- 未经用户明确要求，不执行 `git commit`、`git push`、merge、rebase、reset、clean 或 force push。
- 推送后必须验证 `HEAD == origin/dev`，并检查该 exact HEAD 的 GitHub Actions。
- 普通任务：`NOT_STARTED → IMPLEMENTED|SELF_REVIEWED → COMMITTED|CI_PENDING → ACCEPTED|CI_GREEN`。
- 高风险任务：`NOT_STARTED → IMPLEMENTED|PENDING_REVIEW → REVIEW_ACCEPTED|READY_TO_COMMIT → COMMITTED|CI_PENDING → ACCEPTED|CI_GREEN`。
- CI 失败固定写 `COMMITTED|CI_FAILED|FIX_REQUIRED`；修复必须 review、新 commit、push，再回到 `COMMITTED|CI_PENDING`。
- exact-head CI 已绿但同 batch 仍有授权工作时写 `COMMITTED|CI_GREEN|CONTINUE_REQUIRED`，不得提前 accepted 或初始化下一 batch。
- 回滚已推送提交使用 `git revert <sha>`，不得改写共享历史。

## 8. Phase 1 work batch 与 evidence

- Phase 1 顺序固定为 `PHASE_1_PLAN`、`PHASE_1_1` 至 `PHASE_1_7`、`PHASE_1_CLOSEOUT`；精确长名称读取 machine contract 与 active plan。
- 每个 numbered work batch 只做一个主要目标，控制在 1 至 2 个工作日，可独立回滚、测试、review 和 exact-head CI。
- 当前 batch 未 `ACCEPTED|CI_GREEN` 前不得启动下一 batch。
- 高风险、authority、release/closeout 与真实 AutoCAD integration 使用 `docs/current/evidence/phase-1/<TASK-ID>.attempt-<NN>.md`；两位 attempt 不覆盖。
- 普通低风险任务不强制独立 evidence；机械 commit/push 不创建空 evidence。
- AutoCAD runtime、DWG read/write 与 write/script 安全事实只能由真实实现和对应 evidence 推进，不能由计划、mock 或 CI 推断。

## 9. 收尾输出

每次交付必须说明：范围、变更文件、关键 diff、验证命令与结果、未验证项、安全影响、回滚方式、后续问题和工具声明。
