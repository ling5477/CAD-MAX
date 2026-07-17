# AutoCAD SDK setup

## Phase 0 status

CadMax.AutoCAD.Plugin targets net8.0-windows and compiles without Autodesk assemblies.
It contains lifecycle and document-context interfaces only. It is not an operational
AutoCAD plugin and cannot be loaded to edit DWG files.

## Why DLLs are local-only

AutoCAD managed assemblies are proprietary, version-specific, and installed under a
licensed Autodesk product. This repository must never contain AcDbMgd.dll, AcMgd.dll,
AcCoreMgd.dll, Autodesk SDK archives, redistributables, or personal installation paths.

## Planned Phase 1 approach

1. Install a licensed AutoCAD 2025 or 2026 instance locally.
2. Create an ignored machine-local MSBuild properties file containing the SDK root.
3. Add a separate SDK-bound adapter project or conditional build target.
4. Reference Autodesk assemblies with Copy Local disabled.
5. Keep the default solution graph buildable in CI without AutoCAD.
6. Add a local integration-test profile that runs only when the SDK and AutoCAD are
   explicitly available.

Do not put an absolute Autodesk path in a committed project file. Do not download DLLs
from unofficial mirrors.

## Version boundary

AutoCAD 2025 and 2026 belong to the .NET 8 adapter line. AutoCAD 2024 requires a
separate .NET Framework 4.8 compatibility project in Phase 7; it will not be forced
into the current solution.
