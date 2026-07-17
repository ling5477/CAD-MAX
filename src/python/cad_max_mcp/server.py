"""MCP server registration with only implemented, safe tools."""

from __future__ import annotations

from typing import Any, Literal
from uuid import UUID, uuid4

from mcp.server.fastmcp import FastMCP

from cad_max_mcp.backends import CadBackend
from cad_max_mcp.bridge import create_backend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.tools.system import SystemOperation, cad_system_operation

TransportName = Literal["stdio", "streamable-http"]


def create_server(
    settings: CadMaxSettings | None = None,
    backend: CadBackend | None = None,
    transport: TransportName = "stdio",
) -> FastMCP:
    """Create a CAD-MAX server exposing only real bootstrap capabilities."""
    runtime_settings = settings or CadMaxSettings()
    runtime_backend = backend or create_backend(runtime_settings)
    server = FastMCP(
        "CAD-MAX",
        instructions=(
            "Safe CAD-MAX bootstrap. Real DWG editing is not implemented. "
            "Unimplemented operations fail closed."
        ),
        host=runtime_settings.http_host,
        port=runtime_settings.http_port,
        streamable_http_path=runtime_settings.http_path,
        stateless_http=True,
        json_response=True,
    )

    @server.tool(name="cad_system")
    async def cad_system(
        operation: SystemOperation,
        request_id: UUID | None = None,
        trace_id: UUID | None = None,
    ) -> dict[str, Any]:
        """Inspect health, version, or the truthful capability inventory."""
        result = await cad_system_operation(
            operation=operation,
            request_id=request_id or uuid4(),
            trace_id=trace_id or uuid4(),
            settings=runtime_settings,
            backend=runtime_backend,
            transport=transport,
        )
        return result.to_wire()

    @server.tool(name="drawing")
    async def drawing(
        operation: Literal["status"],
        request_id: UUID | None = None,
        trace_id: UUID | None = None,
    ) -> dict[str, Any]:
        """Return actual drawing status or BACKEND_NOT_CONFIGURED; never fabricate a drawing."""
        del operation
        result = await runtime_backend.drawing_status(
            request_id=request_id or uuid4(),
            trace_id=trace_id or uuid4(),
        )
        return result.to_wire()

    return server
