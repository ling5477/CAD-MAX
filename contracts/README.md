# CAD-MAX protocol contracts

The files in this directory are the language-neutral boundary between the Python MCP
server and the C# localhost bridge.

- cad-command.schema.json defines a versioned command request.
- cad-result.schema.json defines the common result envelope.
- JSON property names are camelCase.
- Every request carries UUID requestId and traceId values.
- Unknown commands fail closed with NOT_IMPLEMENTED.
- Exceptions are mapped to structured status values; stack traces and local paths are
  never part of client responses.

Schema version 1.0 is intentionally present from the first release. Breaking changes
require a new schema version and an ADR.
