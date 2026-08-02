"""System tool implementation kept separate from MCP registration."""

from __future__ import annotations

import platform
from typing import Any, Literal
from uuid import UUID

from cad_max_mcp import SCHEMA_VERSION, SERVER_NAME, __version__
from cad_max_mcp.backends import CadBackend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.models.drawing import DrawingOperation
from cad_max_mcp.models.envelope import ResultEnvelope

SystemOperation = Literal["health", "version", "capabilities"]

DRAWING_CAPABILITIES = tuple(f"drawing.{operation.value}" for operation in DrawingOperation)

IMPLEMENTED_CAPABILITIES = (
    "cad_system.health",
    "cad_system.version",
    "cad_system.capabilities",
    *DRAWING_CAPABILITIES,
)
DEFERRED_CAPABILITIES = (
    "selection",
    "entity",
    "transform",
    "layer",
    "block",
    "annotation",
    "view",
    "export",
    "validation",
    "script",
    "drawing.write",
)


async def cad_system_operation(
    *,
    operation: SystemOperation,
    request_id: UUID,
    trace_id: UUID,
    settings: CadMaxSettings,
    backend: CadBackend,
    transport: str,
) -> ResultEnvelope:
    """Return real server/configuration metadata for the requested operation."""
    probe = await backend.health()
    bridge_result = (
        probe.envelope
        if operation == "health" and probe.envelope is not None
        else await backend.bridge_system(operation)
    )
    common: dict[str, Any] = {
        "serverName": SERVER_NAME,
        "serverVersion": __version__,
        "schemaVersion": SCHEMA_VERSION,
        "transport": transport,
        "bridgeConfigured": probe.configured,
        "bridgeAvailable": probe.available,
        "bridgeStatus": probe.status.value,
        "bridgeConnectionState": backend.connection_state.value,
        "autoCadBridge": bridge_result.to_wire(),
        "readOnly": settings.read_only,
        "allowWrite": settings.allow_write,
        "allowScript": settings.allow_script,
        "allowedRootsCount": len(settings.allowed_roots),
    }
    if operation == "health":
        common["pythonVersion"] = platform.python_version()
        common["bridgeMessage"] = probe.message
        message = "CAD-MAX MCP server is healthy"
    elif operation == "version":
        common["pythonVersion"] = platform.python_version()
        message = "CAD-MAX version information"
    else:
        common["implemented"] = list(IMPLEMENTED_CAPABILITIES)
        common["backendImplemented"] = backend.capabilities()
        common["notImplemented"] = list(DEFERRED_CAPABILITIES)
        message = "CAD-MAX capability inventory"

    return ResultEnvelope.ok(
        request_id=request_id,
        trace_id=trace_id,
        message=message,
        data=common,
    )
