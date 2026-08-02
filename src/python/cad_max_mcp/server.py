"""MCP server registration with only implemented, safe tools."""

from __future__ import annotations

from typing import Annotated, Any, Literal
from uuid import UUID, uuid4

from mcp.server.auth.provider import TokenVerifier
from mcp.server.auth.settings import AuthSettings
from mcp.server.fastmcp import FastMCP
from pydantic import Field

from cad_max_mcp.backends import CadBackend
from cad_max_mcp.bridge import create_backend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.models.drawing import DrawingOperation
from cad_max_mcp.security.mcp_http_auth import (
    MCP_HTTP_READ_SCOPE,
    create_mcp_http_token_verifier,
)
from cad_max_mcp.tools.system import SystemOperation, cad_system_operation

TransportName = Literal["stdio", "streamable-http"]


class CadMaxServer:
    """Own a FastMCP instance whose transport is fixed before any route can be exposed."""

    __slots__ = ("__server", "__transport")

    def __init__(self, server: FastMCP, transport: TransportName) -> None:
        self.__server = server
        self.__transport = transport

    async def list_tools(self) -> list[Any]:
        """Expose the safe registration inventory without exposing the raw server object."""
        return await self.__server.list_tools()

    def streamable_http_app(self) -> Any:
        """Return HTTP routes only when caller authentication was bound at construction."""
        if self.__transport != "streamable-http":
            raise RuntimeError("CAD-MAX server is not configured for Streamable HTTP")
        return self.__server.streamable_http_app()

    def run(self) -> None:
        """Run only the immutable transport selected during construction."""
        self.__server.run(transport=self.__transport)


def create_server(
    settings: CadMaxSettings | None = None,
    backend: CadBackend | None = None,
    transport: TransportName = "stdio",
) -> CadMaxServer:
    """Create a CAD-MAX server exposing only real bootstrap capabilities."""
    runtime_settings = settings or CadMaxSettings()
    runtime_backend = backend or create_backend(runtime_settings)
    token_verifier: TokenVerifier | None = None
    auth: AuthSettings | None = None
    if transport == "streamable-http":
        token_verifier = create_mcp_http_token_verifier(
            runtime_settings.mcp_http_token_file,
            runtime_settings.bridge_token_file,
        )
        origin = _loopback_http_origin(runtime_settings)
        auth = AuthSettings(
            issuer_url=f"{origin}/",
            resource_server_url=f"{origin}{runtime_settings.http_path}",
            required_scopes=[MCP_HTTP_READ_SCOPE],
        )
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
        token_verifier=token_verifier,
        auth=auth,
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
        operation: DrawingOperation,
        expected_document_id: Annotated[
            str | None,
            Field(pattern=r"^doc_[A-Za-z0-9_-]{22}$"),
        ] = None,
        deadline_ms: Annotated[int, Field(ge=100, le=10_000)] = 5000,
        request_id: UUID | None = None,
        trace_id: UUID | None = None,
    ) -> dict[str, Any]:
        """Inspect only allowlisted read-only document metadata through the AutoCAD bridge."""
        result = await runtime_backend.drawing_inspect(
            operation,
            expected_document_id=expected_document_id,
            deadline_ms=deadline_ms,
            request_id=request_id or uuid4(),
            trace_id=trace_id or uuid4(),
        )
        return result.to_wire()

    return CadMaxServer(server, transport)


def _loopback_http_origin(settings: CadMaxSettings) -> str:
    """Construct an RFC-valid local origin without changing the configured bind host."""
    host = settings.http_host
    if ":" in host and not host.startswith("["):
        host = f"[{host}]"
    return f"http://{host}:{settings.http_port}"
