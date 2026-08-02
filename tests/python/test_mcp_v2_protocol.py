"""MCP 2026-07-28 and legacy protocol conformance at the public boundary."""

from __future__ import annotations

import os
import secrets
from collections.abc import Mapping
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx
from mcp.client.session import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client
from mcp.client.streamable_http import streamable_http_client

from cad_max_mcp.backends.base import BackendProbe
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.models import BridgeConnectionState, ResultEnvelope, Status
from cad_max_mcp.models.drawing import DrawingOperation
from cad_max_mcp.server import create_server
from token_file_helpers import write_secure_token_file

PROTOCOL_VERSION = "2026-07-28"
PROTOCOL_META = "io.modelcontextprotocol/protocolVersion"
CAPABILITIES_META = "io.modelcontextprotocol/clientCapabilities"
CLIENT_INFO_META = "io.modelcontextprotocol/clientInfo"


class ProtocolBackend:
    """Safe deterministic backend for protocol-only tests."""

    def __init__(self) -> None:
        self.instance_id = uuid4()
        self.calls: list[tuple[UUID, UUID]] = []

    @property
    def connection_state(self) -> BridgeConnectionState:
        return BridgeConnectionState.CONNECTED

    def capabilities(self) -> list[str]:
        return ["documentContext.available", "drawing.status"]

    async def health(self) -> BackendProbe:
        return BackendProbe(
            configured=True,
            available=True,
            status=Status.OK,
            message="protocol fixture",
            connection_state=self.connection_state,
        )

    async def bridge_system(self, operation: str) -> ResultEnvelope:
        data: dict[str, Any] = {}
        if operation == "capabilities":
            data = {
                "instanceId": str(self.instance_id),
                "capabilityRevision": "fixture-1",
                "pluginState": "READY",
                "autocadConnected": True,
                "developmentHost": False,
                "capabilities": {
                    "documentContext.available": True,
                    "drawing.status": True,
                    "drawing.write": False,
                },
            }
        return ResultEnvelope.ok(
            request_id=uuid4(),
            trace_id=uuid4(),
            message="protocol fixture",
            data=data,
        )

    async def drawing_status(self, request_id: UUID, trace_id: UUID) -> ResultEnvelope:
        return await self.drawing_inspect(
            DrawingOperation.STATUS,
            expected_instance_id=None,
            expected_document_id=None,
            deadline_ms=5000,
            request_id=request_id,
            trace_id=trace_id,
        )

    async def drawing_inspect(
        self,
        operation: DrawingOperation,
        *,
        expected_instance_id: UUID | None,
        expected_document_id: str | None,
        deadline_ms: int,
        request_id: UUID,
        trace_id: UUID,
    ) -> ResultEnvelope:
        del expected_instance_id, expected_document_id, deadline_ms
        assert operation is DrawingOperation.STATUS
        self.calls.append((request_id, trace_id))
        return ResultEnvelope.ok(
            request_id=request_id,
            trace_id=trace_id,
            message="protocol fixture",
            data={
                "instanceId": str(self.instance_id),
                "dispatchId": str(uuid4()),
                "operation": "status",
                "mainThreadVerified": True,
                "executionContext": "APPLICATION_CONTEXT",
                "activeDocumentId": None,
                "queueDelayMs": 0,
                "executionMs": 0,
                "readMode": "READ_ONLY",
                "transactionUsed": False,
                "runtimeState": "READY",
                "documentState": "NO_ACTIVE_DOCUMENT",
                "documentCount": 0,
            },
        )


def _settings(tmp_path: Path) -> tuple[CadMaxSettings, str]:
    token = secrets.token_urlsafe(32)
    token_file = tmp_path / "mcp-caller-token.json"
    write_secure_token_file(
        token_file,
        {
            "schemaVersion": "1.0",
            "token": token,
            "createdAtUtc": "2026-07-28T00:00:00Z",
        },
    )
    return CadMaxSettings(mcp_http_token_file=token_file), token


def _modern_request(
    method: str,
    *,
    request_id: str | int = 1,
    params: Mapping[str, Any] | None = None,
    meta: Mapping[str, Any] | None = None,
) -> tuple[dict[str, str], dict[str, Any]]:
    request_meta = {
        PROTOCOL_META: PROTOCOL_VERSION,
        CAPABILITIES_META: {},
        CLIENT_INFO_META: {"name": "cad-max-conformance", "version": "1.0"},
        **(dict(meta) if meta is not None else {}),
    }
    request_params = {"_meta": request_meta, **(dict(params) if params is not None else {})}
    headers = {
        "MCP-Protocol-Version": PROTOCOL_VERSION,
        "Mcp-Method": method,
        "Accept": "application/json",
        "Content-Type": "application/json",
    }
    if method == "tools/call" and isinstance(request_params.get("name"), str):
        headers["Mcp-Name"] = request_params["name"]
    return headers, {
        "jsonrpc": "2.0",
        "id": request_id,
        "method": method,
        "params": request_params,
    }


async def _post_modern(
    client: httpx.AsyncClient,
    method: str,
    *,
    request_id: str | int = 1,
    params: Mapping[str, Any] | None = None,
    meta: Mapping[str, Any] | None = None,
) -> httpx.Response:
    headers, body = _modern_request(
        method,
        request_id=request_id,
        params=params,
        meta=meta,
    )
    return await client.post("/mcp", headers=headers, json=body)


def _assert_closed_object_schemas(schema: Any) -> None:
    if isinstance(schema, dict):
        if schema.get("type") == "object":
            assert schema.get("additionalProperties") is False
        for value in schema.values():
            _assert_closed_object_schemas(value)
    elif isinstance(schema, list):
        for value in schema:
            _assert_closed_object_schemas(value)


async def test_http_2026_discover_list_and_calls_need_no_initialize(tmp_path: Path) -> None:
    settings, token = _settings(tmp_path)
    backend = ProtocolBackend()
    server = create_server(settings, backend, "streamable-http")
    app = server.streamable_http_app()

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers={"Authorization": f"Bearer {token}"},
        ) as client:
            discover = await _post_modern(client, "server/discover")
            listed = await _post_modern(client, "tools/list", request_id=2)
            system = await _post_modern(
                client,
                "tools/call",
                request_id=3,
                params={"name": "cad_system", "arguments": {"operation": "health"}},
            )
            drawing = await _post_modern(
                client,
                "tools/call",
                request_id=4,
                params={"name": "drawing", "arguments": {"operation": "status"}},
            )
            capabilities = await _post_modern(
                client,
                "tools/call",
                request_id=5,
                params={"name": "cad_system", "arguments": {"operation": "capabilities"}},
            )
            get_response = await client.get(
                "/mcp", headers={"MCP-Protocol-Version": PROTOCOL_VERSION}
            )
            delete_response = await client.delete(
                "/mcp", headers={"MCP-Protocol-Version": PROTOCOL_VERSION}
            )

    for response in (discover, listed, system, drawing, capabilities):
        assert response.status_code == 200
        assert "mcp-session-id" not in response.headers
    assert discover.json()["result"]["supportedVersions"] == [PROTOCOL_VERSION]
    tools = listed.json()["result"]["tools"]
    assert [tool["name"] for tool in tools] == ["cad_system", "drawing"]
    for tool in tools:
        _assert_closed_object_schemas(tool["inputSchema"])
        _assert_closed_object_schemas(tool["outputSchema"])
    assert system.json()["result"]["structuredContent"]["success"] is True
    assert drawing.json()["result"]["structuredContent"]["success"] is True
    public_capabilities = capabilities.json()["result"]["structuredContent"]["data"][
        "autoCadBridge"
    ]["data"]["capabilities"]
    assert public_capabilities == [
        {"name": "documentContext.available", "enabled": True},
        {"name": "drawing.status", "enabled": True},
        {"name": "drawing.write", "enabled": False},
    ]
    assert backend.calls
    assert get_response.status_code == 405
    assert delete_response.status_code == 405
    assert get_response.headers["allow"] == "POST"
    assert delete_response.headers["allow"] == "POST"


async def test_http_2026_version_metadata_body_limit_and_unknown_fields_fail_closed(
    tmp_path: Path,
) -> None:
    settings, token = _settings(tmp_path)
    server = create_server(settings, ProtocolBackend(), "streamable-http")
    app = server.streamable_http_app()
    auth = {"Authorization": f"Bearer {token}"}

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers=auth,
        ) as client:
            headers, body = _modern_request("tools/list")
            missing_version = await client.post(
                "/mcp",
                headers={
                    key: value for key, value in headers.items() if key != "MCP-Protocol-Version"
                },
                json=body,
            )
            headers["MCP-Protocol-Version"] = "2099-01-01"
            body["params"]["_meta"][PROTOCOL_META] = "2099-01-01"
            unsupported = await client.post("/mcp", headers=headers, json=body)
            unknown = await _post_modern(
                client,
                "tools/call",
                params={
                    "name": "drawing",
                    "arguments": {"operation": "status", "trace_id": str(uuid4())},
                },
            )
            oversized = await client.post(
                "/mcp",
                headers={"Content-Type": "application/json", **auth},
                content=b"{" + (b" " * (settings.mcp_max_request_body_bytes + 1)),
            )

    assert missing_version.json()["error"]["code"] == -32602
    assert unsupported.status_code == 400
    assert unsupported.json()["error"]["code"] == -32022
    assert unknown.status_code == 400
    assert unknown.json()["error"]["code"] == -32602
    assert oversized.status_code == 413


async def test_http_2026_request_and_trace_identity_are_bounded(tmp_path: Path) -> None:
    settings, token = _settings(tmp_path)
    backend = ProtocolBackend()
    server = create_server(settings, backend, "streamable-http")
    app = server.streamable_http_app()
    request_id = uuid4()
    trace_id = uuid4()
    valid_traceparent = f"00-{trace_id.hex}-0123456789abcdef-01"

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers={"Authorization": f"Bearer {token}"},
        ) as client:
            valid = await _post_modern(
                client,
                "tools/call",
                request_id=str(request_id),
                params={"name": "drawing", "arguments": {"operation": "status"}},
                meta={"traceparent": valid_traceparent},
            )
            malformed = await _post_modern(
                client,
                "tools/call",
                request_id="not-a-uuid",
                params={"name": "drawing", "arguments": {"operation": "status"}},
                meta={"traceparent": "x" * 4096, "authorization": "ignored"},
            )

    valid_content = valid.json()["result"]["structuredContent"]
    malformed_content = malformed.json()["result"]["structuredContent"]
    assert UUID(valid_content["requestId"]) == request_id
    assert UUID(valid_content["traceId"]) == trace_id
    assert UUID(malformed_content["requestId"]) != request_id
    assert UUID(malformed_content["traceId"]) != trace_id
    assert malformed_content["data"]["instanceId"] == str(backend.instance_id)


async def test_backend_schema_mismatch_is_a_sanitized_structured_error(tmp_path: Path) -> None:
    class InvalidBackend(ProtocolBackend):
        async def drawing_inspect(
            self,
            operation: DrawingOperation,
            *,
            expected_instance_id: UUID | None,
            expected_document_id: str | None,
            deadline_ms: int,
            request_id: UUID,
            trace_id: UUID,
        ) -> ResultEnvelope:
            del operation, expected_instance_id, expected_document_id, deadline_ms
            return ResultEnvelope.ok(
                request_id=request_id,
                trace_id=trace_id,
                message="invalid fixture",
                data={"unexpected": "CADMAX_SECRET_SENTINEL"},
            )

    settings, token = _settings(tmp_path)
    app = create_server(settings, InvalidBackend(), "streamable-http").streamable_http_app()
    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers={"Authorization": f"Bearer {token}"},
        ) as client:
            response = await _post_modern(
                client,
                "tools/call",
                params={"name": "drawing", "arguments": {"operation": "status"}},
            )

    content = response.json()["result"]["structuredContent"]
    assert content["success"] is False
    assert content["status"] == "SCHEMA_MISMATCH"
    assert content["errorCode"] == "SCHEMA_MISMATCH"
    assert content["data"] == {}
    assert "CADMAX_SECRET_SENTINEL" not in response.text


async def test_official_v2_client_uses_discover_and_legacy_initialize(tmp_path: Path) -> None:
    settings, token = _settings(tmp_path)
    server = create_server(settings, ProtocolBackend(), "streamable-http")
    app = server.streamable_http_app()

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers={"Authorization": f"Bearer {token}"},
        ) as client:
            async with streamable_http_client("http://127.0.0.1:47771/mcp", http_client=client) as (
                read,
                write,
            ):
                async with ClientSession(read, write) as modern:
                    discovered = await modern.discover()
                    listed = await modern.list_tools()
                    called = await modern.call_tool("cad_system", {"operation": "health"})
            async with streamable_http_client("http://127.0.0.1:47771/mcp", http_client=client) as (
                read,
                write,
            ):
                async with ClientSession(read, write) as legacy:
                    initialized = await legacy.initialize()
                    legacy_listed = await legacy.list_tools()
                    legacy_called = await legacy.call_tool("drawing", {"operation": "status"})

    assert discovered.supported_versions == [PROTOCOL_VERSION]
    assert {tool.name for tool in listed.tools} == {"cad_system", "drawing"}
    assert called.is_error is False
    assert initialized.protocol_version == "2025-11-25"
    assert {tool.name for tool in legacy_listed.tools} == {"cad_system", "drawing"}
    assert legacy_called.is_error is False


async def test_stdio_2026_requests_need_no_initialize_and_survive_restart() -> None:
    parameters = StdioServerParameters(
        command=str((Path.cwd() / ".venv" / "Scripts" / "cad-max-mcp.exe").resolve()),
        args=["serve", "--transport", "stdio"],
        cwd=str(Path.cwd()),
        env=dict(os.environ),
    )

    async def invoke() -> tuple[list[str], bool, bool]:
        async with stdio_client(parameters) as (read, write):
            async with ClientSession(read, write) as session:
                discovered = await session.discover()
                listed = await session.list_tools()
                called = await session.call_tool("cad_system", {"operation": "health"})
        return [tool.name for tool in listed.tools], called.is_error, bool(discovered)

    first = await invoke()
    second = await invoke()

    assert first == (["cad_system", "drawing"], False, True)
    assert second == first
