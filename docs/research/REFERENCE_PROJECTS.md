# Reference project review

Snapshot date: 2026-07-17.

This review used public repository pages and documentation only. CAD-MAX is an
independent implementation; no source code from these projects was copied.

## Summary

| Project | Language | Communication | CAD backend | Tool organization | Security and tests | License and copying |
| --- | --- | --- | --- | --- | --- | --- |
| [U-C4N/Autocad-MCP](https://github.com/U-C4N/Autocad-MCP) | Python | MCP stdio and HTTP | AutoCAD COM plus headless ezdxf | Large typed catalog grouped by drawing, entity, layer, block, analysis, and validation concerns | Documents path guards, command/AutoLISP controls, HTTP bind/auth controls, COM timeout, and a substantial test suite | MIT observed. License permits reuse with notice, but CAD-MAX copied no code |
| [puran-water/autocad-mcp](https://github.com/puran-water/autocad-mcp) | Python and AutoLISP | MCP stdio; JSON file IPC plus Win32 window messages | AutoCAD LT through AutoLISP and headless ezdxf | Eight consolidated tools such as drawing, entity, layer, block, annotation, view, and system | Tests directory observed. Arbitrary AutoLISP execution is explicitly available and is outside CAD-MAX safe defaults | MIT observed. CAD-MAX uses only the concept of consolidated tool groups; no code copied |
| [daobataotie/CAD-MCP](https://github.com/daobataotie/CAD-MCP) | Python | MCP process to pywin32 COM | AutoCAD, GstarCAD, and ZWCAD COM automation | Server/controller split with drawing and layer-oriented operations | Public README did not document a comparable allowlist, read-only gate, or test suite at review time | MIT observed. No code copied |
| [nguyenngocdue/DeepBIM-MCP-Autocad-Plugin](https://github.com/nguyenngocdue/DeepBIM-MCP-Autocad-Plugin) | C# | TCP 8180 and HTTP 9180 JSON-RPC; documentation also describes tunnel exposure | In-process AutoCAD Managed .NET plugin | Command registry, executor, document-context queue, and command classes | DocumentContextQueue is a useful boundary. Public tunnel exposure is not adopted. No clear repository test suite was observed | No LICENSE file or license statement was identified on the reviewed root page. Copying is not allowed |
| [neka-nat/freecad-mcp](https://github.com/neka-nat/freecad-mcp) | Python | MCP stdio to a FreeCAD add-on RPC server | FreeCAD Python API | MCP tools separated from an in-application add-on | Localhost default and remote IP allowlist are documented. Arbitrary Python execution exists and is not adopted. No repository test directory was observed on the reviewed root page | MIT observed. CAD-MAX copied no code |

## Ideas adopted

- Separate the MCP-facing process from the in-CAD execution boundary.
- Group tools by stable CAD domain instead of publishing one unbounded script tool.
- Use a registry and dispatcher so unregistered commands fail closed.
- Marshal document work through an explicit in-application context queue.
- Treat path policy, loopback binding, timeout, and capability honesty as first-class
  architecture.
- Keep a headless test boundary that does not require a licensed CAD installation.

## Designs explicitly not adopted

- Direct Python COM as the production primary AutoCAD backend.
- AutoLISP, Python, or arbitrary script execution exposed to MCP.
- File IPC in a shared temporary directory as the main transport.
- Public tunnels or 0.0.0.0 default binding.
- Automatic fallback that turns an unavailable real CAD backend into a different
  write-capable engine while still reporting success.
- Broad tool registration before implementation and failure testing.
- Copying code from a repository without an explicit license.

## Copying decision

No reference-project source was copied. MIT repositories could legally permit reuse
subject to their notices, but this bootstrap intentionally uses only independently
implemented architecture ideas. The DeepBIM repository is architecture-reference only
because an explicit license was not identified.
