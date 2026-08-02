"""Streamable HTTP caller-auth tests without a TCP listener or AutoCAD process."""

from __future__ import annotations

import secrets
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx
import pytest
from mcp.client.session import ClientSession
from mcp.client.streamable_http import streamable_http_client

from cad_max_mcp.backends.base import BackendProbe
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.models import BridgeConnectionState, ResultEnvelope, Status
from cad_max_mcp.models.drawing import DrawingOperation
from cad_max_mcp.security.mcp_http_auth import LocalMcpHttpTokenVerifier, McpHttpAuthError
from cad_max_mcp.server import create_server
from token_file_helpers import write_secure_token_file


class RecordingBackend:
    """Harmless backend that proves authentication occurs before tool dispatch."""

    def __init__(self) -> None:
        self.drawing_calls = 0

    @property
    def connection_state(self) -> BridgeConnectionState:
        return BridgeConnectionState.CONNECTED

    def capabilities(self) -> list[str]:
        return []

    async def health(self) -> BackendProbe:
        return BackendProbe(
            configured=True,
            available=True,
            status=Status.OK,
            message="mock backend",
            connection_state=self.connection_state,
        )

    async def bridge_system(self, operation: str) -> ResultEnvelope:
        del operation
        return ResultEnvelope.ok(
            request_id=uuid4(),
            trace_id=uuid4(),
            message="mock backend",
        )

    async def drawing_status(self, request_id: UUID, trace_id: UUID) -> ResultEnvelope:
        return await self.drawing_inspect(
            DrawingOperation.STATUS,
            expected_document_id=None,
            deadline_ms=5000,
            request_id=request_id,
            trace_id=trace_id,
        )

    async def drawing_inspect(
        self,
        operation: DrawingOperation,
        *,
        expected_document_id: str | None,
        deadline_ms: int,
        request_id: UUID,
        trace_id: UUID,
    ) -> ResultEnvelope:
        del operation, expected_document_id, deadline_ms
        self.drawing_calls += 1
        return ResultEnvelope.ok(
            request_id=request_id,
            trace_id=trace_id,
            message="mock backend",
            data={"readOnly": True},
        )


def write_token_file(tmp_path: Path, *, name: str, token: str | None = None) -> tuple[Path, str]:
    """Create the bounded machine-local token format without disclosing it in assertions."""
    value = token or secrets.token_urlsafe(32)
    path = tmp_path / name
    write_secure_token_file(
        path,
        {
            "schemaVersion": "1.0",
            "token": value,
            "createdAtUtc": "2026-07-24T00:00:00Z",
        },
    )
    return path, value


def initialize_request() -> dict[str, Any]:
    return {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
            "protocolVersion": "2025-11-25",
            "capabilities": {},
            "clientInfo": {"name": "cad-max-test", "version": "1.0"},
        },
    }


async def test_streamable_http_verifier_rejects_non_ascii_bearer_without_raising() -> None:
    verifier = LocalMcpHttpTokenVerifier(secrets.token_urlsafe(32))

    assert await verifier.verify_token("non-ascii-bearer-\u00e9") is None


async def test_streamable_http_rejects_missing_and_wrong_callers_before_backend(
    tmp_path: Path,
) -> None:
    token_file, _ = write_token_file(tmp_path, name="mcp-caller-token.json")
    backend = RecordingBackend()
    server = create_server(
        CadMaxSettings(mcp_http_token_file=token_file),
        backend,
        "streamable-http",
    )
    app = server.streamable_http_app()

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
        ) as client:
            missing = await client.post("/mcp", json=initialize_request())
            wrong = await client.post(
                "/mcp",
                json=initialize_request(),
                headers={"Authorization": "Bearer wrong-token"},
            )

    assert missing.status_code == 401
    assert wrong.status_code == 401
    assert backend.drawing_calls == 0


async def test_streamable_http_valid_caller_can_initialize_and_call_read_only_tool(
    tmp_path: Path,
) -> None:
    token_file, token = write_token_file(tmp_path, name="mcp-caller-token.json")
    backend = RecordingBackend()
    server = create_server(
        CadMaxSettings(mcp_http_token_file=token_file),
        backend,
        "streamable-http",
    )
    app = server.streamable_http_app()

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers={"Authorization": f"Bearer {token}"},
        ) as client:
            async with streamable_http_client(
                "http://127.0.0.1:47771/mcp",
                http_client=client,
            ) as (read, write, _):
                async with ClientSession(read, write) as session:
                    await session.initialize()
                    result = await session.call_tool("drawing", {"operation": "status"})

    assert result.isError is False
    assert backend.drawing_calls == 1


def test_streamable_http_requires_token_and_rejects_bridge_token_reuse(tmp_path: Path) -> None:
    backend = RecordingBackend()
    with pytest.raises(McpHttpAuthError, match="MCP_HTTP_AUTH_NOT_CONFIGURED"):
        create_server(CadMaxSettings(), backend, "streamable-http")

    bridge_file, shared_token = write_token_file(tmp_path, name="bridge-token.json")
    caller_file, _ = write_token_file(
        tmp_path,
        name="mcp-caller-token.json",
        token=shared_token,
    )
    with pytest.raises(McpHttpAuthError, match="MCP_HTTP_AUTH_TOKEN_REUSED"):
        create_server(
            CadMaxSettings(
                bridge_token_file=bridge_file,
                mcp_http_token_file=caller_file,
            ),
            backend,
            "streamable-http",
        )
