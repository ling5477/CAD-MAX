"""MCP server registration with only implemented, safe tools."""

from __future__ import annotations

import re
from collections.abc import Mapping
from typing import Annotated, Any, Literal
from uuid import UUID, uuid4

from mcp.server import MCPServer
from mcp.server.auth.provider import TokenVerifier
from mcp.server.auth.settings import AuthSettings
from mcp.server.context import CallNext, HandlerResult, ServerRequestContext
from mcp.server.mcpserver import Context
from mcp.server.transport_security import TransportSecuritySettings
from mcp.shared.exceptions import MCPError
from mcp_types import INVALID_PARAMS
from pydantic import Field, ValidationError

from cad_max_mcp import __version__
from cad_max_mcp.backends import CadBackend
from cad_max_mcp.bridge import create_backend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.models.drawing import DrawingOperation
from cad_max_mcp.models.envelope import ResultEnvelope, Status
from cad_max_mcp.models.tool_output import CadSystemToolResult, DrawingToolResult
from cad_max_mcp.security.mcp_http_auth import (
    MCP_HTTP_READ_SCOPE,
    create_mcp_http_token_verifier,
)
from cad_max_mcp.tools.system import SystemOperation, cad_system_operation

TransportName = Literal["stdio", "streamable-http"]
TRACEPARENT_PATTERN = re.compile(r"^00-(?P<trace_id>[0-9a-f]{32})-[0-9a-f]{16}-(?:00|01)$")
TOOL_ARGUMENTS = {
    "cad_system": frozenset({"operation"}),
    "drawing": frozenset(
        {
            "operation",
            "expected_instance_id",
            "expected_document_id",
            "deadline_ms",
        }
    ),
}


class ClosedToolInputMiddleware:
    """Publish closed tool schemas and reject unknown arguments before dispatch."""

    async def __call__(
        self,
        ctx: ServerRequestContext[Any, Any],
        call_next: CallNext,
    ) -> HandlerResult:
        request_headers = getattr(ctx.request, "headers", None)
        if (
            request_headers is not None
            and ctx.method != "initialize"
            and request_headers.get("mcp-protocol-version") is None
        ):
            raise MCPError(INVALID_PARAMS, "MCP-Protocol-Version header is required")

        if ctx.method == "tools/call" and isinstance(ctx.params, Mapping):
            name = ctx.params.get("name")
            arguments = ctx.params.get("arguments", {})
            allowed = TOOL_ARGUMENTS.get(name) if isinstance(name, str) else None
            if allowed is not None and isinstance(arguments, Mapping):
                if any(not isinstance(key, str) or key not in allowed for key in arguments):
                    raise MCPError(INVALID_PARAMS, "Invalid tool arguments")

        result = await call_next(ctx)
        if ctx.method == "tools/list":
            tools = (
                result.get("tools") if isinstance(result, dict) else getattr(result, "tools", None)
            )
            if isinstance(tools, list):
                for tool in tools:
                    schema = (
                        tool.get("inputSchema")
                        if isinstance(tool, dict)
                        else getattr(tool, "input_schema", None)
                    )
                    if isinstance(schema, dict):
                        schema["additionalProperties"] = False
        return result


class CadMaxServer:
    """Own an MCPServer whose authenticated transport is fixed before exposure."""

    __slots__ = ("__server", "__settings", "__transport", "__transport_security")

    def __init__(
        self,
        server: MCPServer,
        transport: TransportName,
        settings: CadMaxSettings,
    ) -> None:
        self.__server = server
        self.__transport = transport
        self.__settings = settings
        self.__transport_security = _transport_security(settings)

    async def list_tools(self) -> list[Any]:
        """Expose the safe registration inventory without exposing the raw server object."""
        tools = await self.__server.list_tools()
        for tool in tools:
            tool.input_schema["additionalProperties"] = False
        return tools

    def streamable_http_app(self) -> Any:
        """Return HTTP routes only when caller authentication was bound at construction."""
        if self.__transport != "streamable-http":
            raise RuntimeError("CAD-MAX server is not configured for Streamable HTTP")
        return self.__server.streamable_http_app(
            host=self.__settings.http_host,
            streamable_http_path=self.__settings.http_path,
            stateless_http=True,
            json_response=True,
            max_request_body_size=self.__settings.mcp_max_request_body_bytes,
            transport_security=self.__transport_security,
        )

    def run(self) -> None:
        """Run only the immutable transport selected during construction."""
        if self.__transport == "stdio":
            self.__server.run(transport="stdio")
            return
        self.__server.run(
            transport="streamable-http",
            host=self.__settings.http_host,
            port=self.__settings.http_port,
            streamable_http_path=self.__settings.http_path,
            stateless_http=True,
            json_response=True,
            max_request_body_size=self.__settings.mcp_max_request_body_bytes,
            transport_security=self.__transport_security,
        )


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
    server = MCPServer(
        "CAD-MAX",
        version=__version__,
        instructions=(
            "CAD-MAX read-only AutoCAD MCP server. Document-level DWG inspection is "
            "implemented; DWG editing is not implemented."
        ),
        token_verifier=token_verifier,
        auth=auth,
        middleware=[ClosedToolInputMiddleware()],
    )

    @server.tool(name="cad_system", structured_output=True)
    async def cad_system(
        operation: SystemOperation,
        ctx: Context,
    ) -> CadSystemToolResult:
        """Inspect health, version, or the truthful capability inventory."""
        request_id, trace_id = _request_identifiers(ctx)
        result = await cad_system_operation(
            operation=operation,
            request_id=request_id,
            trace_id=trace_id,
            settings=runtime_settings,
            backend=runtime_backend,
            transport=transport,
        )
        try:
            return CadSystemToolResult.model_validate(_public_cad_system_wire(result.to_wire()))
        except ValidationError:
            return CadSystemToolResult.model_validate(
                _schema_failure(request_id, trace_id).to_wire()
            )

    @server.tool(name="drawing", structured_output=True)
    async def drawing(
        operation: DrawingOperation,
        ctx: Context,
        expected_instance_id: UUID | None = None,
        expected_document_id: Annotated[
            str | None,
            Field(pattern=r"^doc_[A-Za-z0-9_-]{22}$"),
        ] = None,
        deadline_ms: Annotated[int, Field(ge=100, le=10_000)] = 5000,
    ) -> DrawingToolResult:
        """Inspect only allowlisted read-only document metadata through the AutoCAD bridge."""
        request_id, trace_id = _request_identifiers(ctx)
        result = await runtime_backend.drawing_inspect(
            operation,
            expected_instance_id=expected_instance_id,
            expected_document_id=expected_document_id,
            deadline_ms=deadline_ms,
            request_id=request_id,
            trace_id=trace_id,
        )
        try:
            return DrawingToolResult.model_validate(result.to_wire())
        except ValidationError:
            return DrawingToolResult.model_validate(_schema_failure(request_id, trace_id).to_wire())

    return CadMaxServer(server, transport, runtime_settings)


def _loopback_http_origin(settings: CadMaxSettings) -> str:
    """Construct an RFC-valid local origin without changing the configured bind host."""
    host = settings.http_host
    if ":" in host and not host.startswith("["):
        host = f"[{host}]"
    return f"http://{host}:{settings.http_port}"


def _transport_security(settings: CadMaxSettings) -> TransportSecuritySettings:
    """Pin DNS-rebinding validation to the configured loopback endpoint."""
    origin = _loopback_http_origin(settings)
    host = origin.removeprefix("http://")
    return TransportSecuritySettings(
        enable_dns_rebinding_protection=True,
        allowed_hosts=[host],
        allowed_origins=[origin],
    )


def _request_identifiers(ctx: Context) -> tuple[UUID, UUID]:
    """Map only bounded protocol identity and W3C trace metadata to internal UUIDs."""
    raw_request_id = ctx.request_context.request_id
    request_id = uuid4()
    if isinstance(raw_request_id, str) and len(raw_request_id) == 36:
        try:
            candidate = UUID(raw_request_id)
        except ValueError:
            pass
        else:
            if str(candidate) == raw_request_id.lower() and candidate.int != 0:
                request_id = candidate

    trace_id = uuid4()
    meta = ctx.request_context.meta
    if isinstance(meta, Mapping):
        traceparent = meta.get("traceparent")
        if isinstance(traceparent, str) and len(traceparent) == 55:
            match = TRACEPARENT_PATTERN.fullmatch(traceparent)
            if match is not None:
                candidate = UUID(hex=match.group("trace_id"))
                if candidate.int != 0:
                    trace_id = candidate
    return request_id, trace_id


def _public_cad_system_wire(wire: dict[str, Any]) -> dict[str, Any]:
    """Project the internal capability map into a closed, stable public list."""
    data = wire.get("data")
    if not isinstance(data, dict):
        return wire
    bridge = data.get("autoCadBridge")
    if not isinstance(bridge, dict):
        return wire
    bridge_data = bridge.get("data")
    if not isinstance(bridge_data, dict):
        return wire
    capabilities = bridge_data.get("capabilities")
    if not isinstance(capabilities, dict):
        return wire
    if not all(
        isinstance(name, str) and isinstance(enabled, bool)
        for name, enabled in capabilities.items()
    ):
        return wire

    public_wire = dict(wire)
    public_data = dict(data)
    public_bridge = dict(bridge)
    public_bridge_data = dict(bridge_data)
    public_bridge_data["capabilities"] = [
        {"name": name, "enabled": enabled} for name, enabled in sorted(capabilities.items())
    ]
    public_bridge["data"] = public_bridge_data
    public_data["autoCadBridge"] = public_bridge
    public_wire["data"] = public_data
    return public_wire


def _schema_failure(request_id: UUID, trace_id: UUID) -> ResultEnvelope:
    """Replace backend schema details with a stable closed failure envelope."""
    return ResultEnvelope.failure(
        request_id=request_id,
        trace_id=trace_id,
        status=Status.SCHEMA_MISMATCH,
        message="Backend response did not match the public tool schema",
    )
