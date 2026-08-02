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

Streamable HTTP requires a dedicated machine-local caller bearer token. Create a second,
independent token file with the existing secure local-token provisioning workflow, set its
absolute path in `CAD_MAX_MCP_HTTP_TOKEN_FILE`, and keep it different from
`CAD_MAX_BRIDGE_TOKEN_FILE`. CAD-MAX rejects a missing, malformed, reused, or same-path
caller token. The caller token is not forwarded to the C# bridge.

Start after configuring that variable:

    uv run cad-max-mcp serve --transport streamable-http

Connect the MCP client to:

    http://127.0.0.1:47771/mcp

Send the token only as an HTTP `Authorization: Bearer <caller-token>` header. The token file
uses the same bounded JSON token format as the local bridge token, but it must contain a
separately generated value. Do not put either token in MCP configuration, logs, command-line
arguments, or this repository.

The current configuration rejects non-loopback hosts. Do not forward this port through
a tunnel or expose it to a LAN.

## Expected tools

- cad_system with health, version, and capabilities operations.
- drawing with status, list_documents, active_document, units, bounds, layouts, and
  system_metadata operations when a real accepted bridge advertises them.

No entity, layer, block, annotation, export, validation, write, or script tool should appear.

## Expected no-backend result

When CAD_MAX_BRIDGE_URL is unset, drawing status returns a structured
BACKEND_NOT_CONFIGURED envelope. That response is correct and does not indicate an
installation failure.
