# CAD-MAX Codex Project Instructions

本文件可复制到 Codex Project Instructions。它定义稳定执行规则，不复制具体 current Phase 或 next action；
每轮必须从 [STATUS.md](STATUS.md) 动态解析。

## 1. 项目背景

CAD-MAX 通过 Python MCP server、loopback HTTP/JSON 与 AutoCAD 进程内 C# plugin 连接
AutoCAD 2025/2026。Python 负责 MCP/validation，C# plugin 负责 AutoCAD Managed .NET API、
主线程、document context、Transaction 与对象证据。未实现 capability 必须 fail closed。

## 2. Source priority

1. 安全、凭证、Autodesk 许可证与用户本轮明确要求；
2. `docs/current/STATUS.md` 的唯一 `cad-max-current-authority` 区块；
3. current `ROADMAP.md` 的下一允许动作；
4. `AGENTS.md`、active plan、`GOVERNANCE_WORKFLOW.md` 与 machine contract；
5. architecture/security/capability 文档与代码/测试；
6. `TESTING.md`、`WORKLOG.md`、attempt evidence 仅作为历史证据。

current 文档冲突时输出 `BLOCKED / CURRENT_AUTHORITY_CONFLICT`，不得猜测旧状态。

## 3. Task classification

每轮至少选择一个主分类：

- `CODE_ANALYSIS`、`CODE_CHANGE`、`BUG_FIX`、`TESTING`；
- `ARCHITECTURE`、`DOCUMENTATION`、`DOCS_GOVERNANCE`；
- `AUTOCAD_PLUGIN`、`PROTOCOL_CONTRACT`、`SECURITY_REVIEW`；
- `CI_CD`、`GITHUB_PUBLISH`、`REAL_AUTOCAD_INTEGRATION`。

以下按高风险处理：Autodesk SDK/reference、plugin loading、`.bundle`、loopback auth、protocol/schema、
thread/document context、Transaction/Undo、DWG write、script/AutoLISP、CI workflow、token、installer、
path permissions、logging/redaction、release、P0/P1 修复。

## 4. Scope preflight

写操作前必须输出 repository、branch、current authority、authorized next action、target files、excluded files、
docs budget、expected output 与 rollback boundary，并执行：

```powershell
Get-Location
git status --short
git branch --show-current
```

commit/push 任务还要执行 `git fetch origin`、核对 `HEAD == origin/dev` 与 GitHub authentication。
不得覆盖用户已有改动，不扫描任务无关目录，不修改下一 work batch。

## 5. Docs budget

- 普通代码任务默认不改 docs；确需记录时最多向 `WORKLOG.md` 追加一条事实；
- 测试基线变化可追加 `TESTING.md` 与 `WORKLOG.md`；
- Phase/work-batch 接受或 Phase closeout 才更新 `STATUS.md` 与 current `ROADMAP.md`；
- root README 只在入口、架构、启动方式或 Phase 总状态变化时更新；
- 专项 PLAN 只用于安全、CI、协议、SDK、发布、write、migration 等高风险 epic；
- review-only 默认 no-diff；
- 不为机械 commit/push 创建 evidence，不为每个中间状态复制多份 current docs；
- ledger 与 attempt evidence append-only，历史失败不得改写。

## 6. Lifecycle

普通任务：

```text
NOT_STARTED
→ IMPLEMENTED|SELF_REVIEWED
→ COMMITTED|CI_PENDING
→ ACCEPTED|CI_GREEN
```

高风险任务：

```text
NOT_STARTED
→ IMPLEMENTED|PENDING_REVIEW
→ REVIEW_ACCEPTED|READY_TO_COMMIT
→ COMMITTED|CI_PENDING
→ ACCEPTED|CI_GREEN
```

CI 失败固定为 `COMMITTED|CI_FAILED|FIX_REQUIRED`；修复、review、新 commit 后回到
`COMMITTED|CI_PENDING`。不得只重跑、删除检查、降低 analyzer、跳过测试或 failed → accepted。

若 exact-head CI 绿色但同 numbered work batch 仍有授权工作，使用
`COMMITTED|CI_GREEN|CONTINUE_REQUIRED`；不得推进 accepted batch 或初始化下一 batch。

## 7. Validation

最低本地验证：

```powershell
git diff --check
.\scripts\docs\verify-docs.ps1
.\scripts\verify.ps1
```

按改动补充 failure/boundary/regression。真实 AutoCAD 未运行时必须写 `NOT_RUN`；SDK-free test 或 mock
不能写成 AutoCAD/DWG 已验证。文档检查标题、链接、路径、术语、authority 与禁止范围。

## 8. Git / exact-head CI

- 日常分支为 `dev`，不修改 `main`；
- 使用 Conventional Commits；不自动 merge/rebase/reset/clean/force push；
- push 后验证 `git rev-parse HEAD` 与 `git rev-parse origin/dev`；
- 只接受该 SHA 的全部 required GitHub Actions jobs；
- `CI_PENDING`、`CI_FAILED` 与 `CI_GREEN` 不得混用；
- 已推送回滚使用 `git revert <sha>`，revert commit 也需 exact-head CI。

## 9. AutoCAD 安全边界

- 默认 `readOnly=true`、`allowWrite=false`、`allowScript=false`、`httpHost=127.0.0.1`；
- 不提交 Autodesk DLL/SDK、许可证数据、`.env`、token、绝对个人路径；
- Python 不以 COM 绕过 C# Bridge；
- AutoCAD API 只在进程内 plugin 的正确主线程/document context 使用；
- 外部 HTTP 有 token、timeout、cancellation、queue/concurrency limit、structured error 与 redaction；
- 未实现 operation 不注册或返回 `NOT_IMPLEMENTED`；
- Phase 1 只允许 `READ`、`PLAN`、`PREVIEW`，不允许 DWG mutation；
- 真实 integration 只使用获许可 AutoCAD 与可丢弃 Golden DWG，不使用客户图纸。

## 10. 默认输出格式

```text
Task classification:
Risk classification:
Repository / branch:
Current authority / authorized next action:
Scope / excluded scope / docs budget:
Files inspected:
Files changed:
Key diff:
Validation:
Review result:
Commit / push / exact-head CI:
Security impact:
Unverified:
Rollback:
Next concrete action:
Tools declaration:
```

未执行的命令、未完成的 CI 或未运行的 AutoCAD integration 必须原样报告，不得推断为通过。
