# CAD-MAX roadmap

This file defines the capability sequence. Current Phase status and the only allowed
next action are authoritative in [current STATUS](current/STATUS.md) and
[current ROADMAP](current/ROADMAP.md).

## Phase 0: Repository Bootstrap

Python MCP transports, versioned contracts, fail-closed backends, .NET bridge core,
localhost Host, SDK-free plugin boundary, tests, CI, and security documentation.

## Phase 1: AutoCAD Connection

Bind AutoCAD 2025/2026 managed assemblies through local configuration. Implement
plugin lifecycle, localhost bridge registration, health, version, document-context
queue, cancellation, and truthful connection status. No general write tools.

## Phase 2: Read-Only Drawing Inspection

Read active-document metadata, units, layers, blocks, selections, and bounded entity
summaries. Add pagination, size limits, document identity without leaking full paths,
and disposable-fixture integration tests.

## Phase 3: Basic Entity Editing

Add explicitly authorized line, circle, polyline, text, move, copy, rotate, and delete
operations. Require readOnly false, allowWrite true, validation, document locks,
transactions, undo marks, and idempotency where applicable.

## Phase 4: Layer / Block / Annotation

Add bounded layer, block, dimension, text, and annotation operations with standards
validation and failure-path tests.

## Phase 5: Transaction / Undo / Rollback

Formalize command groups, transaction scopes, Undo integration, cancellation before
commit, rollback evidence, and partial-failure reporting.

## Phase 6: Export / Validation / Installer

Add controlled export, drawing validation, packaging, signed installer planning, and
upgrade/uninstall behavior.

## Phase 7: AutoCAD 2024 Compatibility

Create a separate .NET Framework 4.8 compatibility solution. Do not mix its runtime
and dependency constraints into the .NET 8 projects.

## Phase 8: Other CAD Adapters

Evaluate separate adapters for other CAD applications. Each adapter must preserve the
contract, security defaults, capability honesty, and independent test boundary.
