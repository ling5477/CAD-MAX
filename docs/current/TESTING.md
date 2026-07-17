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
