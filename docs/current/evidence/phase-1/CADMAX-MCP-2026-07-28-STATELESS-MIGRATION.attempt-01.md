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
