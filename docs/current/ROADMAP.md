# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_4_READONLY_DOCUMENT_INSPECTION`

只允许完成以下单一 work batch：

1. 在已接受的 bounded main-thread/document-context dispatch 上建立只读 inspection 边界；
2. 实现受控的 runtime/document list、active document、units、bounds、layouts 和 system metadata；
3. 保持 opaque document identity、document lifecycle 与 active routing fail-closed；
4. 保持已接受的认证 loopback listener、token、端口、queue/backpressure 与 capability honesty 不回退；
5. 保持默认 solution/CI 在无 AutoCAD、无 Autodesk DLL 时可构建和测试。

## 当前禁止

- 枚举实体、图层、块、样式或 selection，或修改 DWG；
- 实现 Phase 1.5 及任何后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
