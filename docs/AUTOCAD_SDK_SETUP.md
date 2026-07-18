# AutoCAD SDK setup

## Phase 1.1 boundary

`CadMax.AutoCAD.Plugin` 是可在默认 solution 中构建和测试的 SDK-free core；
`CadMax.AutoCAD.Adapter` 是显式的本机 SDK-bound 工程，不加入 `CadMax.sln`。adapter 只实现
`IExtensionApplication`、转发生命周期，并注册无参数命令 `CADMAXPLUGINSTATUS`。本批次不访问
`Document`、`Database` 或 `Editor`，不启动 listener、thread 或 timer，也不改变 MCP 工具列表。

固定安全状态为 `readOnly=true`、`allowWrite=false`、`allowScript=false`。真实 plugin load 不代表
Python MCP 已连接 AutoCAD；在 loopback Bridge 完成前 `autocad_runtime` 仍为 `NOT_CONNECTED`。

## Local-only Autodesk references

AutoCAD Managed .NET assemblies 是专有且与产品版本绑定的本机文件。仓库、默认 CI、bundle 和构建
产物都不得包含 `AcMgd.dll`、`AcDbMgd.dll` 或 `AcCoreMgd.dll`，也不得记录 SDK root、`acad.exe`
绝对路径、许可证数据或 Autodesk 账户信息。禁止从 NuGet、第三方镜像或缓存目录回退获取这些 DLL。

检测本机获许可安装只读取 Autodesk 标准安装位置和官方注册表项，不修改注册表：

```powershell
.\scripts\autocad\Get-AutoCADInstallations.ps1
```

输出仅包含年份、产品版本、三项 Managed DLL 文件版本、`.NET 8` 边界结论和 `REDACTED` 路径。

## Generate the ignored local props

为一个已检测到的目标年份生成 machine-local props：

```powershell
.\scripts\autocad\New-LocalSdkProps.ps1 -AutoCADYear 2025
```

生成文件固定为 `.tools/autocad/CadMax.AutoCAD.Local.props`，已被 Git 忽略。安全结构示例见
[`scripts/autocad/CadMax.AutoCAD.Local.props.example`](../scripts/autocad/CadMax.AutoCAD.Local.props.example)；
示例不含真实路径。若文件已存在，脚本默认 fail closed；只有明确替换本机配置时才使用 `-Force`。

## Build and validate the adapter

以下命令验证 props 位于 allowlisted 目录、SDK root 位于仓库外、`acad.exe` 与三项 DLL 均存在、
文件版本匹配年份，而且来源能对应本机检测到的 AutoCAD 安装。随后以 `x64`、
`net8.0-windows`、`TreatWarningsAsErrors=true` 构建 adapter：

```powershell
.\scripts\autocad\Test-AutoCADSdkBoundary.ps1 -AutoCADYear 2025
.\scripts\autocad\Build-AutoCADAdapter.ps1 -AutoCADYear 2025
```

缺少配置、版本不匹配或来源不可验证时均以非零退出码失败。输出只写入已忽略的
`.tools/autocad/build/<year>/`，并在成功前确认 Autodesk DLL 数量为 0。默认 SDK-free 验证仍使用：

```powershell
.\scripts\verify.ps1
```

## Development bundle

仓库只提交 `PackageContents.xml.template` 与生成脚本；生成产物位于
`.tools/autocad/bundles/CAD-MAX-<year>.bundle`：

```powershell
.\scripts\autocad\New-DevBundle.ps1 -AutoCADYear 2025
.\scripts\autocad\Test-DevBundle.ps1 -AutoCADYear 2025
```

校验器要求固定 ProductCode/UpgradeCode、项目版本、目标 Series、相对 `ModuleName`、精确 status
command、adapter assembly 存在且 Autodesk DLL 数量为 0。2025 与 2026 目前分别生成独立 bundle，
不推断一个共用 bundle 已通过双版本矩阵。

Series 来源：

- AutoCAD 2025 本机产品 release 为 `R25.0`；
- Autodesk 2026 `RuntimeRequirements Element Reference` 明确列出 AutoCAD 2026 的 Series 为
  `R25.1`；
- manifest 结构与安装目录遵循 Autodesk 官方
  [About Installing and Uninstalling Plug-In Applications](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-5E50A846-C80B-4FFD-8DD3-C20B22098008.htm)、
  [PackageContents.xml Format Reference](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-BC76355D-682B-46ED-B9B7-66C95EEF2BD0.htm) 和
  [RuntimeRequirements Element Reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-1591CA01-EF87-48CD-952B-772FE26037F1.htm)。

## Current-user install and uninstall

先正常关闭 AutoCAD。安装只允许当前用户 Autodesk `ApplicationPlugins` 目录，且要求显式确认：

```powershell
.\scripts\autocad\Install-DevBundle.ps1 -AutoCADYear 2025 -ConfirmTarget
```

脚本会对 source、安装 staging 和最终目标分别验证 manifest、identity、adapter、Series 与 Autodesk
DLL=0；检测到既有 CAD-MAX bundle 时先保留精确 rollback 副本。它不修改注册表、`SECURELOAD` 或
`TRUSTEDPATHS`，不写机器级目录。

AutoCAD 正常关闭后，使用精确年份和固定 ProductCode 卸载：

```powershell
.\scripts\autocad\Uninstall-DevBundle.ps1 -AutoCADYear 2025 -ConfirmTarget
```

卸载脚本只删除精确命名且 identity 匹配的 CAD-MAX bundle，不接受通配符，不删除 SDK props、
lifecycle evidence、rollback 副本或任何第三方插件。删除 bundle 文件不等于从当前 AutoCAD 进程热卸载；
本批次的 unload evidence 只能来自正常宿主终止时的 `IExtensionApplication.Terminate`。

## Lifecycle evidence

插件只向 `%LOCALAPPDATA%\CAD-MAX\evidence\plugin-lifecycle.jsonl` 追加 allowlisted 单行 UTF-8 JSON。
文件上限为 1 MiB；达到上限或发生 IO 异常时停止写入，但异常不会逃逸到 AutoCAD。记录不包含 DWG、
document title、用户名、机器名、路径、环境变量、stack trace 或原始异常消息。

## Rollback

1. 正常关闭 AutoCAD；
2. 用 `Uninstall-DevBundle.ps1` 移除当前年份的 CAD-MAX bundle；
3. 如需恢复旧版本，先人工核验 rollback 目录中的 CAD-MAX ProductCode，再做精确目录恢复；否则保持未安装；
4. 验证当前用户 `ApplicationPlugins` 中不存在错误版本，再启动 AutoCAD；
5. 不使用通配符删除、`git reset --hard`、注册表修改或安全设置降级。

AutoCAD 2024 属于后续独立的 .NET Framework 4.8 compatibility project，不进入当前 `.NET 8`
adapter line。
