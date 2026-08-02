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
from mcp.server.auth.provider import AccessToken

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
        self.instance_id = uuid4()

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
        self.drawing_calls += 1
        assert operation is DrawingOperation.STATUS
        return ResultEnvelope.ok(
            request_id=request_id,
            trace_id=trace_id,
            message="mock backend",
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


def modern_request(
    method: str, *, name: str | None = None
) -> tuple[dict[str, str], dict[str, Any]]:
    version = "2026-07-28"
    params: dict[str, Any] = {
        "_meta": {
            "io.modelcontextprotocol/protocolVersion": version,
            "io.modelcontextprotocol/clientCapabilities": {},
        }
    }
    if name is not None:
        params.update({"name": name, "arguments": {"operation": "status"}})
    headers = {"MCP-Protocol-Version": version, "Mcp-Method": method}
    if name is not None:
        headers["Mcp-Name"] = name
    return headers, {"jsonrpc": "2.0", "id": 1, "method": method, "params": params}


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


async def test_streamable_http_auth_protects_all_modern_protocol_methods(
    tmp_path: Path,
) -> None:
    token_file, _ = write_token_file(tmp_path, name="mcp-caller-token.json")
    backend = RecordingBackend()
    app = create_server(
        CadMaxSettings(mcp_http_token_file=token_file),
        backend,
        "streamable-http",
    ).streamable_http_app()

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
        ) as client:
            responses = []
            for method, name in (
                ("server/discover", None),
                ("tools/list", None),
                ("tools/call", "drawing"),
            ):
                headers, body = modern_request(method, name=name)
                responses.append(await client.post("/mcp", headers=headers, json=body))
            headers, body = modern_request("tools/list")
            malformed = await client.post(
                "/mcp",
                headers={**headers, "Authorization": "Basic malformed"},
                json=body,
            )

    assert all(response.status_code == 401 for response in responses)
    assert malformed.status_code == 401
    assert backend.drawing_calls == 0


async def test_streamable_http_scope_mismatch_is_forbidden_and_token_is_redacted(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    caplog: pytest.LogCaptureFixture,
) -> None:
    token_file, token = write_token_file(tmp_path, name="mcp-caller-token.json")

    class WrongScopeVerifier:
        async def verify_token(self, candidate: str) -> AccessToken | None:
            if candidate != token:
                return None
            return AccessToken(
                token=candidate,
                client_id="wrong-scope-fixture",
                scopes=["cad-max:write"],
            )

    monkeypatch.setattr(
        "cad_max_mcp.server.create_mcp_http_token_verifier",
        lambda token_file, bridge_token_file: WrongScopeVerifier(),
    )
    backend = RecordingBackend()
    app = create_server(
        CadMaxSettings(mcp_http_token_file=token_file),
        backend,
        "streamable-http",
    ).streamable_http_app()
    headers, body = modern_request("tools/list")

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
        ) as client:
            response = await client.post(
                "/mcp",
                headers={**headers, "Authorization": f"Bearer {token}"},
                json=body,
            )

    assert response.status_code == 403
    assert token not in caplog.text
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
            ) as (read, write):
                async with ClientSession(read, write) as session:
                    await session.initialize()
                    result = await session.call_tool("drawing", {"operation": "status"})

    assert result.is_error is False
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
