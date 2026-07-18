# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`CONTINUE_PHASE_1_MAINTENANCE_DEPENDENCIES`

只允许完成以下单一 work batch：

1. 将 `Microsoft.AspNetCore.Mvc.Testing` 更新到 `8.0.29`；
2. 将所有直接引用的 `xunit.runner.visualstudio` 更新到 `3.1.5`；
3. 受控更新对应 lockfile，并验证 test discovery、warnings 与 adapter load；
4. Group B exact-head CI 绿色后关闭 #7/#9；
5. 单独升级并验证 `Microsoft.NET.Test.Sdk 18.8.1`，不得混入其他依赖。

## 当前禁止

- 启动或实现 Phase 1.2 的 listener、token、health/version/capabilities 或 lifecycle；
- 访问 document API、active document 或读取/修改 DWG；
- 实现 document context queue 或任何后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
