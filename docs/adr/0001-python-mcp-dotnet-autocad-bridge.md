# ADR 0001: Python MCP with a C# AutoCAD bridge

Status: Accepted

Date: 2026-07-17

## Context

CAD-MAX needs MCP stdio and Streamable HTTP support while eventually executing inside
AutoCAD 2025/2026 with correct managed API, thread, document, transaction, and Undo
semantics.

## Decision

Use a Python 3.12 MCP process for protocol and client-facing validation. Use a
versioned localhost HTTP/JSON contract to a .NET 8 C# bridge. Put all future AutoCAD
Managed .NET API calls behind an in-process plugin adapter.

Do not use Python COM as the production primary backend. Do not combine AutoCAD 2024
.NET Framework constraints with the .NET 8 solution.

## Consequences

Benefits:

- official MCP Python SDK transport support;
- explicit trust and process boundaries;
- Autodesk types do not leak into public contracts;
- CI builds without AutoCAD;
- cancellation, timeout, and structured error mapping can be tested without DWG.

Costs:

- two language toolchains;
- a versioned IPC contract;
- lifecycle coordination between MCP, Host, plugin, and AutoCAD;
- future integration tests require a licensed local AutoCAD environment.

## Guardrails

- loopback bindings only in the bootstrap;
- read-only and scripts disabled by default;
- no unimplemented success stubs;
- no Autodesk binaries in Git;
- every command carries requestId and traceId;
- unknown commands return NOT_IMPLEMENTED.
