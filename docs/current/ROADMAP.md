# 当前路线

本文件只定义下一允许动作，不覆盖 [STATUS.md](STATUS.md)。完整 Phase 1 范围和后续 numbered work batch
见 [PHASE_1_AUTOCAD_CONNECTION_PLAN.md](PHASE_1_AUTOCAD_CONNECTION_PLAN.md)。

## 当前允许动作

`IMPLEMENT_PHASE_1_1_AUTOCAD_PLUGIN_BOOTSTRAP`

只允许完成以下单一 work batch：

1. 建立 AutoCAD 2025/2026、.NET 8 的 plugin 项目与编译边界；
2. 设计并验证获许可本机 Autodesk DLL 的未提交引用方式，`Copy Local` 关闭；
3. 建立开发 `.bundle` / `PackageContents.xml` 与 load/unload 生命周期证据；
4. 保持默认 solution/CI 在无 AutoCAD、无 Autodesk DLL 时可构建和测试；
5. 对 SDK 缺失、版本不匹配、仓库内二进制/绝对路径做 fail-closed 检查。

## 当前禁止

- 读取或修改 DWG、创建 Golden DWG 或注册业务 MCP operation；
- 实现 loopback listener、document context queue 或后续 numbered work batch；
- 提交 Autodesk DLL/SDK、bundle 生成物、本机路径、许可证数据或凭证；
- 使用 COM、AutoLISP、command string、script、ObjectARX；
- 放宽 read-only、write/script disabled 或 loopback-only 默认值。
