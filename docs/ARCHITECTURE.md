# CAD-MAX architecture

## Decision summary

CAD-MAX separates protocol orchestration from CAD execution:

    MCP client
        |
    Python MCP process
        |
    localhost HTTP / JSON contract
        |
    C# bridge and AutoCAD plugin
        |
    AutoCAD document

The common contract is versioned from day one and has no Autodesk type dependency.

## Why the MCP layer is Python

The official MCP Python SDK provides the required stdio and Streamable HTTP transports,
typed tool schemas, and a mature testing path. Python is well suited to protocol
composition, client integrations, validation, and future adapter selection. uv gives
deterministic dependency locking and fast isolated environments.

The Python layer is not allowed to bypass the bridge for production CAD execution.
It exposes only capabilities backed by real implementations.

## Why the AutoCAD execution layer is C#

AutoCAD 2025 and 2026 use modern .NET for managed plugins. The C# layer can use the
supported AutoCAD Managed .NET API inside the AutoCAD process and can respect
application context, document context, document locking, transactions, and editor
lifecycle rules.

The repository currently has only SDK-independent interfaces. Autodesk assemblies
will be referenced through local, uncommitted configuration in Phase 1.

## Why Python COM is not the production backend

COM can be useful for prototypes, but it does not provide the same explicit document
context and managed transaction model as an in-process AutoCAD plugin. COM calls can
hang, depend on desktop/session state, and make cancellation and thread-affinity
failures difficult to contain. CAD-MAX therefore does not use direct Python COM as its
primary architecture.

## Main-thread and document-context risk

AutoCAD objects are not general-purpose thread-safe data. Future handlers must:

1. Validate and authorize the request before entering AutoCAD.
2. Queue execution to an AutoCAD-supported application or document context.
3. Acquire the correct document lock when required.
4. Start the smallest possible database transaction.
5. Avoid network calls while holding a document lock or transaction.
6. Commit only after all invariants pass; otherwise abort and map the error.
7. Release locks, transactions, and queue resources on every path.

The CadMax.AutoCAD.Plugin interfaces reserve these boundaries without pretending that
the SDK integration already exists.

## Trust boundaries

### MCP client to Python

Input is untrusted. Pydantic validates configuration and tool arguments. Only two
implemented tools are registered. HTTP is loopback-only. The default process has no
file authority because allowedRoots is empty.

### Python to bridge

The bridge URL must resolve to loopback. Requests use a bounded timeout and carry
schemaVersion, requestId, and traceId. Network failures are mapped to stable statuses.
Raw HTTP errors, paths, environment values, and stack traces do not cross back to MCP.

### Bridge Host to AutoCAD plugin

Phase 0 registers no CAD command handlers. Future registration is explicit, and an
unknown command returns NOT_IMPLEMENTED. The Host is a development/test process, not a
public service.

### Plugin to DWG

Future DWG access is the highest-risk boundary. Write handlers will require explicit
allowWrite, readOnly false, per-command classification, document context, transactions,
undo grouping, and tests on disposable drawings.

## Future transaction and Undo design

Phase 5 will define:

- one command to one bounded transaction by default;
- explicit transaction groups only for audited composite operations;
- an AutoCAD Undo mark around each user-visible mutation;
- validation before commit;
- cancellation before commit causing abort;
- result data that identifies the operation, not proprietary document paths;
- no external HTTP call while a document lock or database transaction is held;
- compensating status when AutoCAD reports a partial or non-rollbackable side effect.

No transaction or Undo capability is advertised in Phase 0.
