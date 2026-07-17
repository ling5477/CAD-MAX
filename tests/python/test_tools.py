"""Truthful MCP tool behavior tests."""

from __future__ import annotations

from uuid import uuid4

from cad_max_mcp.backends import NullCadBackend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.models import Status
from cad_max_mcp.tools.system import cad_system_operation


async def test_health_tool_reports_runtime_and_safe_defaults() -> None:
    """Health is about the MCP server and reports the absent bridge separately."""
    result = await cad_system_operation(
        operation="health",
        request_id=uuid4(),
        trace_id=uuid4(),
        settings=CadMaxSettings(),
        backend=NullCadBackend(),
        transport="stdio",
    )

    assert result.status is Status.OK
    assert result.data["serverName"] == "CAD-MAX"
    assert result.data["transport"] == "stdio"
    assert result.data["bridgeConfigured"] is False
    assert result.data["readOnly"] is True
    assert result.data["allowWrite"] is False


async def test_capabilities_distinguish_implemented_from_deferred() -> None:
    """Capability discovery cannot advertise unregistered write operations."""
    result = await cad_system_operation(
        operation="capabilities",
        request_id=uuid4(),
        trace_id=uuid4(),
        settings=CadMaxSettings(),
        backend=NullCadBackend(),
        transport="streamable-http",
    )

    implemented = result.data["implemented"]
    not_implemented = result.data["notImplemented"]
    assert "drawing.status" in implemented
    assert "drawing.write" in not_implemented
    assert "script" in not_implemented
