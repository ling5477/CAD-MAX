# CAD-MAX

CAD-MAX is a safety-first foundation for connecting an MCP client to AutoCAD through
a Python MCP server and a localhost C# bridge.

Current version: 0.1.0, Phase 0 repository bootstrap.

## Current real completion

Implemented now:

- Python 3.12 package cad-max-mcp with stdio and Streamable HTTP transports.
- MCP tools cad_system for health, version, and capabilities.
- MCP tool drawing with the read-only status operation.
- NullCadBackend for machines without AutoCAD.
- AutoCadBridgeBackend with bounded localhost HTTP calls and structured failures.
- Shared versioned JSON contracts and matching Python/C# models.
- .NET 8 bridge dispatcher, localhost development Host, and test suite.
- A compilable net8.0-windows AutoCAD plugin boundary with no Autodesk references.
- Read-only, write-disabled, script-disabled, loopback-only defaults.

Not implemented:

- Real AutoCAD attachment.
- Reading a live drawing.
- Creating, modifying, saving, exporting, or validating DWG content.
- Any script, AutoLISP, arbitrary code, write, or remote-network tool.

CAD-MAX does not currently modify DWG files. When no bridge is configured,
drawing status returns BACKEND_NOT_CONFIGURED. The development bridge has no AutoCAD
command handlers, so an unknown command returns NOT_IMPLEMENTED rather than fake success.

## Architecture

    MCP client
        |
        | stdio or Streamable HTTP at 127.0.0.1:47771/mcp
        v
    Python cad-max-mcp
        |
        | localhost HTTP and versioned JSON
        v
    C# CadMax.Bridge.Host at 127.0.0.1:47770
        |
        | future AutoCAD Managed .NET API adapter
        v
    AutoCAD 2025 or 2026 plugin
        |
        v
    2D DWG document context

Python owns the MCP protocol and client-facing validation. C# will own all future
AutoCAD execution because Autodesk's supported managed API, application context, and
document context belong inside the AutoCAD process. See
[Architecture](docs/ARCHITECTURE.md) and [ADR 0001](docs/adr/0001-python-mcp-dotnet-autocad-bridge.md).

## Supported baseline

- Windows 10 or 11 for the future AutoCAD bridge.
- Python 3.12.
- uv.
- .NET 8 SDK.
- AutoCAD 2025 or 2026 for future plugin integration.
- 2D DWG scope only.

AutoCAD 2024, AutoCAD LT, ZWCAD, GstarCAD, Tianzheng, SolidWorks, FreeCAD, 3D
modeling, and cloud batch processing are explicitly outside this phase.

## Local setup

Install Python 3.12, uv, and the .NET 8 SDK. Then run:

    uv sync --frozen
    uv run cad-max-mcp doctor

The doctor command emits structured JSON and returns exit code 0 when the safe base
configuration is valid.

## Start the MCP server

Stdio:

    uv run cad-max-mcp serve --transport stdio

Streamable HTTP:

    uv run cad-max-mcp serve --transport streamable-http

The HTTP endpoint is http://127.0.0.1:47771/mcp. Configuration rejects 0.0.0.0 and
other non-loopback hosts.

PowerShell wrappers are also available:

    .\scripts\run-mcp-stdio.ps1
    .\scripts\run-mcp-http.ps1

## Start the development bridge

The bridge does not require AutoCAD and exposes only health, capabilities, and a
fail-closed command dispatcher:

    dotnet run --project src/dotnet/CadMax.Bridge.Host

Endpoints:

- GET http://127.0.0.1:47770/health
- GET http://127.0.0.1:47770/v1/capabilities
- POST http://127.0.0.1:47770/v1/commands

No CAD command handler is registered in Phase 0.

## Test and verify

Python:

    uv sync --frozen
    uv run ruff check .
    uv run ruff format --check .
    uv run mypy src/python
    uv run pytest

.NET:

    dotnet restore src/dotnet/CadMax.sln --locked-mode --configfile NuGet.Config
    dotnet build src/dotnet/CadMax.sln --configuration Release --no-restore
    dotnet test src/dotnet/CadMax.sln --configuration Release --no-build

Unified PowerShell verification:

    .\scripts\verify.ps1

The script stops with a non-zero exit code on the first failed check.

## MCP client configuration example

Replace the path with your local checkout:

    {
      "mcpServers": {
        "cad-max": {
          "command": "uv",
          "args": [
            "--directory",
            "C:\\path\\to\\CAD-MAX",
            "run",
            "cad-max-mcp",
            "serve",
            "--transport",
            "stdio"
          ]
        }
      }
    }

More examples are in [MCP client setup](docs/MCP_CLIENT_SETUP.md).

## Configuration and safe defaults

Environment variables use the CAD_MAX_ prefix. The committed .env.example contains
names and safe example values only. A real .env file is ignored and is not loaded
implicitly by the application.

- readOnly is true.
- allowWrite is false.
- allowScript is false.
- httpHost is 127.0.0.1.
- allowedRoots is empty.
- bridgeUrl is unset.

Future file operations must normalize an absolute path and prove it is inside an
allowed root. An empty allowlist grants no file access.

## Why Autodesk DLLs are not in this repository

AcDbMgd.dll, AcMgd.dll, AcCoreMgd.dll, Autodesk SDK files, and AutoCAD redistributables
are proprietary and installation-specific. They must be referenced from a licensed
local AutoCAD installation through uncommitted machine-local MSBuild configuration.
CI does not install AutoCAD and does not need these DLLs. See
[AutoCAD SDK setup](docs/AUTOCAD_SDK_SETUP.md).

## Security warning

CAD automation can alter valuable drawings. Keep the HTTP services on loopback, keep
writes and scripts disabled, never expose the development Host through a tunnel, and
work on backed-up test drawings when a future write phase is enabled. See
[Security model](docs/SECURITY.md).

## Roadmap

The ordered delivery plan is in [ROADMAP.md](docs/ROADMAP.md). Phase 1 will establish
a real AutoCAD 2025/2026 connection and lifecycle boundary without enabling arbitrary
DWG editing.

## License

MIT. See [LICENSE](LICENSE).
