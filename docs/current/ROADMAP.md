# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_3_DOCUMENT_CONTEXT_DISPATCH`

只允许完成以下单一 work batch：

1. 建立 application/document context queue 与 AutoCAD 主线程 dispatch 边界；
2. 建立 active document routing，并对 document 切换、缺失和失效做 fail-closed 处理；
3. 覆盖 timeout、cancellation、heartbeat、modal/busy 与 shutdown 竞态；
4. 保持已接受的认证 loopback listener、token、端口与 capability honesty 不回退；
5. 保持默认 solution/CI 在无 AutoCAD、无 Autodesk DLL 时可构建和测试。

## 当前禁止

- 读取 document/DWG 内容、枚举实体/图层/布局，或修改 DWG；
- 实现 Phase 1.4 及任何后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
