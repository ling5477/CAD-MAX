# Development guide

## Toolchain

- Git and GitHub CLI
- Python 3.12
- uv
- .NET 8 SDK
- PowerShell 7 recommended on Windows

AutoCAD is not required for repository-level build and test.

## Bootstrap

    uv sync --frozen
    dotnet restore src/dotnet/CadMax.sln --locked-mode --configfile NuGet.Config

Or:

    .\scripts\bootstrap.ps1

## Verification

    .\scripts\verify.ps1

The order is dependency sync, Ruff lint, Ruff format check, mypy, pytest, doctor,
.NET restore, .NET build, and .NET tests. Native non-zero exit codes stop the script.

## Package boundaries

- src/python/cad_max_mcp owns MCP, configuration, backend selection, and client-safe
  response models.
- contracts owns language-neutral JSON Schema.
- CadMax.Contracts mirrors the JSON contract in C#.
- CadMax.Bridge.Core owns validation, command registration, dispatch, timeout,
  cancellation, and exception mapping.
- CadMax.Bridge.Host owns localhost HTTP development endpoints.
- CadMax.AutoCAD.Plugin reserves the future SDK integration boundary.

Do not move Autodesk types into Contracts or Bridge.Core.

## Safe configuration

Configuration is read from CAD_MAX_ environment variables. The application does not
load .env automatically. allowedRoots accepts a JSON array of absolute paths when set
through the environment.

Example for a temporary PowerShell process:

    $env:CAD_MAX_ALLOWED_ROOTS = '["C:\\CAD-Test-Drawings"]'
    uv run cad-max-mcp doctor

Never commit the resulting environment values.

## Status contract

The stable statuses are OK, BACKEND_UNAVAILABLE, BACKEND_NOT_CONFIGURED,
INVALID_ARGUMENT, PATH_NOT_ALLOWED, READ_ONLY, NOT_IMPLEMENTED, TIMEOUT, and
INTERNAL_ERROR.

Every command carries requestId and traceId UUIDs. MCP tools generate them when a
client does not provide them. A bridge request must provide both.

## Adding tools

A tool is registered only when its real backend exists. A planned operation belongs in
the notImplemented capability list, not in the MCP tool registry. Script execution and
write operations remain unregistered in this phase.
