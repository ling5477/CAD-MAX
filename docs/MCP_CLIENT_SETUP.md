# MCP client setup

## Stdio

Use uv to run the installed console command from the repository:

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

Stdio is the safest default because no TCP listener is created. Logs are written to
stderr so they do not corrupt MCP protocol stdout.

## Streamable HTTP

Start:

    uv run cad-max-mcp serve --transport streamable-http

Connect the MCP client to:

    http://127.0.0.1:47771/mcp

The current configuration rejects non-loopback hosts. Do not forward this port through
a tunnel or expose it to a LAN.

## Expected tools

- cad_system with health, version, and capabilities operations.
- drawing with the status operation.

No entity, layer, block, annotation, export, validation, write, or script tool should
appear in Phase 0.

## Expected no-backend result

When CAD_MAX_BRIDGE_URL is unset, drawing status returns a structured
BACKEND_NOT_CONFIGURED envelope. That response is correct and does not indicate an
installation failure.
