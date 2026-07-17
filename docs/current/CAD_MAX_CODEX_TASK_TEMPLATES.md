# CAD-MAX Codex Task Templates

模板只提供少量稳定任务形态。使用前必须动态读取 [STATUS.md](STATUS.md) 与 current
[ROADMAP.md](ROADMAP.md)，将 `<WORK_BATCH>`、`<NEXT_ACTION>`、路径和验证命令替换为真实值。

所有模板默认：正文中文为主；状态 token、代码符号、路径、命令与 protocol field 保留英文；docs 默认不改，
如需记录只允许 `WORKLOG.md` 一条，除非模板明确授权更高 docs budget。

## 1. 普通代码任务

```text
Task classification: CODE_CHANGE
Risk: ordinary
Goal: <single goal>
Target files: <files>
Excluded: AutoCAD runtime、DWG write、unrelated modules
Docs budget: 默认不改 docs；必要时 WORKLOG 一条
Lifecycle: NOT_STARTED → IMPLEMENTED|SELF_REVIEWED → COMMITTED|CI_PENDING → ACCEPTED|CI_GREEN
Validation: focused tests + scripts/verify.ps1
Output: diff、验证、未验证、回滚、下一动作
```

## 2. 高风险 AutoCAD 插件任务

```text
Task classification: AUTOCAD_PLUGIN + SECURITY_REVIEW
Risk: high
Work batch: <WORK_BATCH>
Goal: <one plugin/SDK/context objective>
Preflight: authority/branch/clean scope/SDK source/loopback defaults
Excluded: 下一 work batch、DWG write、script、Autodesk binary commit
Docs budget: attempt evidence；authority 只在 acceptance closeout 更新
Lifecycle: NOT_STARTED → IMPLEMENTED|PENDING_REVIEW → REVIEW_ACCEPTED|READY_TO_COMMIT → COMMITTED|CI_PENDING → ACCEPTED|CI_GREEN
Validation: SDK-free CI + explicit local AutoCAD matrix；未运行写 NOT_RUN
Stop: unsafe SDK reference、non-loopback、write/script registration、P0/P1
```

## 3. Review-only

```text
Task classification: CODE_ANALYSIS / SECURITY_REVIEW
Mode: review-only / no-diff
Scope: <commits/files>
Review: authority、architecture、security defaults、failure paths、tests、rollback
Output: P0/P1/P2/P3 findings with file/line evidence
Docs budget: no docs, no evidence unless user explicitly requests durable review
禁止: 修改文件、commit、push、状态推进
```

## 4. CI blocker fix

```text
Task classification: CI_CD + BUG_FIX
Current status: COMMITTED|CI_FAILED|FIX_REQUIRED
Input: exact failed SHA、run ID、failed job/log
Goal: local reproduce → RCA → minimal fix → review → full verify → new commit/push
禁止: 只重跑、continue-on-error、删检查、降 analyzer、跳测试、failed → accepted、force push
Expected state: COMMITTED|CI_PENDING；随后验证新 SHA exact-head CI
Docs budget: 原 attempt 保留；失败/修复事实按既有 evidence 追加
```

## 5. Phase work-batch closeout

```text
Task classification: DOCS_GOVERNANCE + GITHUB_PUBLISH
Work batch: <WORK_BATCH>
Precondition: implementation/review/local verify complete
Steps: candidate commit/push → candidate exact-head CI → evidence/authority sync → closeout commit/push → closeout exact-head CI
Authority: accepted batch 只在 candidate CI success 后推进；下一 batch 初始化为 NOT_STARTED
Docs budget: STATUS、current ROADMAP、TESTING/WORKLOG append、existing attempt
Output: candidate SHA/run/jobs、closeout SHA/run/jobs、final authority、rollback
```

## 6. Phase closeout

```text
Task classification: ARCHITECTURE + DOCS_GOVERNANCE + SECURITY_REVIEW
Risk: high
Precondition: all numbered work batches ACCEPTED|CI_GREEN；real AutoCAD matrix and capability honesty complete
Review: P0/P1、Autodesk binary、token/log redaction、write/script absence、Golden DWG evidence、rollback
Flow: Phase candidate CI → authority closeout → closeout exact-head CI
禁止: 在 closeout 混入下一 Phase implementation
```

## 7. Security review

```text
Task classification: SECURITY_REVIEW
Mode: review-only unless fix explicitly requested
Scope: loopback/token、schema、queue、thread/document context、path/log、SDK/license、write/script gates
Output: P0/P1/P2/P3、触发条件、最坏结果、最小修复、验证、回滚
Hard blocker: non-loopback、secret/Autodesk binary、capability false success、write/script default regression
```

## 8. Real AutoCAD integration test

```text
Task classification: REAL_AUTOCAD_INTEGRATION
Risk: high; explicit opt-in only
Environment: licensed AutoCAD 2025 or 2026、isolated Windows account、disposable Golden DWG
Preflight: version、plugin/build SHA、bundle、token、port、process、fixture digest
Test: load/unload、health、document context、read operation、timeout/cancel、disconnect/restart、cleanup
禁止: customer DWG、DWG mutation、production path、secret log、mock-as-real
Output: NOT_RUN / PASS / FAIL per case；structured evidence and cleanup result
```

## 9. Commit / push / exact-head CI

```text
Task classification: GITHUB_PUBLISH + CI_CD
Preflight: dev、clean scoped diff、review accepted、full verify pass、no secret/Autodesk binary
Commit: Conventional Commit；只 stage task files
Push: git push origin dev
Verify: fetch；HEAD == origin/dev；query runs by exact SHA；wait all required jobs
State: pending=COMMITTED|CI_PENDING；failure=COMMITTED|CI_FAILED|FIX_REQUIRED；success only then ACCEPTED|CI_GREEN
Rollback: git revert <sha>，再验证 revert exact-head CI
```

不得为每种小任务新增模板；新需求优先组合上述模板并在 task 中缩小范围。
