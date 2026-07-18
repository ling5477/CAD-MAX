# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_2_LOOPBACK_BRIDGE_LIFECYCLE`

只允许完成以下单一 work batch：

1. 在 AutoCAD plugin 内建立只绑定 `127.0.0.1` 的 HTTP 生命周期边界；
2. 建立 token 校验与 health/version/capabilities 最小协议；
3. 验证 listener start/stop、端口占用、异常退出和资源清理；
4. 保持默认 solution/CI 在无 AutoCAD、无 Autodesk DLL 时可构建和测试；
5. 对非 loopback 绑定、缺失/错误 token、重复启动和清理失败做 fail-closed 检查。

## 当前禁止

- 访问 document API、active document 或读取/修改 DWG；
- 实现 document context queue 或任何后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
