# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`PHASE_1_MAINTENANCE_DEPENDENCIES_CI_WAIT_OR_INVESTIGATION`

只允许完成以下单一 work batch：

1. 提交并推送 Group A 候选到 `dev`；
2. 核对候选 SHA 与 `origin/dev`、GitHub Actions run 的 `headSha` 精确一致；
3. 要求 Governance、Python 3.12、.NET 8 全部成功；
4. 精确记录 Node runtime annotations 的 Action 来源；
5. CI 绿色后关闭 #1–#4，并继续同一 maintenance batch 的 .NET 依赖组。

## 当前禁止

- 启动或实现 Phase 1.2 的 listener、token、health/version/capabilities 或 lifecycle；
- 访问 document API、active document 或读取/修改 DWG；
- 实现 document context queue 或任何后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
