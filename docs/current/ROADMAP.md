# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_5_READONLY_OBJECT_INSPECTION`

只允许完成以下单一 work batch：

1. 在已接受的只读 document inspection 边界上建立 object inspection contract；
2. 实现受控的 entities、layers、blocks、attributes 和 styles 读取；
3. 为对象结果建立 paging、stable sorting、result limits 与语言无关 DTO/schema；
4. 保持 opaque identity、document lifecycle、active routing 与 read transaction fail-closed；
5. 保持默认 solution/CI 在无 AutoCAD、无 Autodesk DLL 时可构建和测试。

## 当前禁止

- 实现 selection、preview、accuracy/revision/fingerprint 或修改 DWG；
- 实现 Phase 1.6 及任何后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 绑定非 loopback 地址、绕过 token，或放宽 read-only、write/script disabled 默认值。
