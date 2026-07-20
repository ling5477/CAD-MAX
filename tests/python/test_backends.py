"""Authenticated Python bridge behavior without a real AutoCAD installation."""

from __future__ import annotations

import json
import secrets
from collections.abc import Callable
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx

from cad_max_mcp.backends import AutoCadBridgeBackend, NullCadBackend
from cad_max_mcp.backends.autocad_bridge import (
    EXPECTED_TRUE_CAPABILITIES,
    REQUIRED_FALSE_CAPABILITIES,
)
from cad_max_mcp.models import BridgeConnectionState, Status


def write_token_file(tmp_path: Path, token: str | None = None) -> tuple[Path, str]:
    """Create a per-test random token fixture."""
    encoded = token or secrets.token_urlsafe(32)
    path = tmp_path / "bridge-token.json"
    path.write_text(
        json.dumps(
            {
                "schemaVersion": "1.0",
                "token": encoded,
                "createdAtUtc": "2026-07-19T00:00:00Z",
            }
        ),
        encoding="utf-8",
    )
    return path, encoded


def endpoint_data(
    path: str,
    *,
    instance_id: UUID,
    revision: str = "revision-1",
    heartbeat_sequence: int = 1,
    development_host: bool = False,
) -> dict[str, Any]:
    common = {
        "instanceId": str(instance_id),
        "autocadConnected": not development_host,
        "developmentHost": development_host,
    }
    if path == "/v1/health":
        return {
            **common,
            "service": "CadMax.Bridge.Host" if development_host else "CadMax.AutoCAD.Bridge",
            "pluginState": "READY",
            "documentAccess": False,
            "dwgRead": False,
            "dwgWrite": False,
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
        }
    if path == "/v1/version":
        return {
            **common,
            "protocolVersion": "1.0",
            "schemaVersion": "1.0",
            "pluginVersion": "0.1.0",
            "adapterVersion": "0.1.0",
            "autocadYear": 0 if development_host else 2025,
            "autocadProductVersion": "NOT_CONNECTED" if development_host else "R25.0.58.0.0",
            "runtimeTarget": "net8.0-windows",
            "capabilityRevision": revision,
        }
    if path == "/v1/capabilities":
        return {
            **common,
            "capabilityRevision": revision,
            "pluginState": "READY",
            "capabilities": {
                **{name: True for name in EXPECTED_TRUE_CAPABILITIES},
                **{name: False for name in REQUIRED_FALSE_CAPABILITIES},
            },
        }
    if path == "/v1/heartbeat":
        return {
            **common,
            "heartbeatSequence": heartbeat_sequence,
            "timestampUtc": "2026-07-19T00:00:00Z",
            "uptimeMs": 100,
            "pluginState": "READY",
            "capabilityRevision": revision,
        }
    raise AssertionError("unexpected route")


def envelope(data: dict[str, Any]) -> dict[str, Any]:
    return {
        "schemaVersion": "1.0",
        "requestId": str(uuid4()),
        "traceId": str(uuid4()),
        "success": True,
        "status": "OK",
        "errorCode": None,
        "message": "safe",
        "data": data,
        "warnings": [],
        "durationMs": 0,
    }


def error_envelope(status: str) -> dict[str, Any]:
    return {
        "schemaVersion": "1.0",
        "requestId": str(uuid4()),
        "traceId": str(uuid4()),
        "success": False,
        "status": status,
        "errorCode": status,
        "message": "safe failure",
        "data": {},
        "warnings": [],
        "durationMs": 0,
    }


def working_handler(
    token: str,
    *,
    instance_id: UUID | None = None,
    development_host: bool = False,
) -> Callable[[httpx.Request], httpx.Response]:
    current_instance = instance_id or uuid4()
    heartbeat = 0

    def handle(request: httpx.Request) -> httpx.Response:
        nonlocal heartbeat
        assert request.headers["authorization"] == f"Bearer {token}"
        if request.url.path == "/v1/heartbeat":
            heartbeat += 1
        return httpx.Response(
            200,
            json=envelope(
                endpoint_data(
                    request.url.path,
                    instance_id=current_instance,
                    heartbeat_sequence=max(1, heartbeat),
                    development_host=development_host,
                )
            ),
        )

    return handle


async def test_null_backend_reports_not_configured() -> None:
    backend = NullCadBackend()

    probe = await backend.health()
    result = await backend.drawing_status(uuid4(), uuid4())

    assert probe.status is Status.BACKEND_NOT_CONFIGURED
    assert probe.connection_state is BridgeConnectionState.NOT_CONFIGURED
    assert result.status is Status.BACKEND_NOT_CONFIGURED
    assert result.success is False


async def test_bearer_header_and_valid_health(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(working_handler(token)),
    )

    probe = await backend.health()

    assert probe.status is Status.OK
    assert probe.available is True
    assert probe.connection_state is BridgeConnectionState.CONNECTED


async def test_missing_token_file_does_not_attempt_network(tmp_path: Path) -> None:
    called = False

    def unexpected(request: httpx.Request) -> httpx.Response:
        nonlocal called
        called = True
        return httpx.Response(500)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        tmp_path / "missing.json",
        transport=httpx.MockTransport(unexpected),
    )

    probe = await backend.health()

    assert probe.status is Status.TOKEN_NOT_CONFIGURED
    assert probe.connection_state is BridgeConnectionState.NOT_CONFIGURED
    assert called is False


async def test_unauthorized_and_route_not_found_are_preserved(tmp_path: Path) -> None:
    token_file, _ = write_token_file(tmp_path)
    responses = iter(
        [
            httpx.Response(401, json=error_envelope("UNAUTHORIZED")),
            httpx.Response(404, json=error_envelope("ROUTE_NOT_FOUND")),
        ]
    )
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(lambda request: next(responses)),
    )

    unauthorized = await backend.bridge_system("health")
    missing_route = await backend.bridge_system("version")

    assert unauthorized.status is Status.UNAUTHORIZED
    assert missing_route.status is Status.ROUTE_NOT_FOUND
    assert backend.connection_state is BridgeConnectionState.INCOMPATIBLE


async def test_timeout_and_connection_failure_are_sanitized(tmp_path: Path) -> None:
    token_file, _ = write_token_file(tmp_path)

    def fail_timeout(request: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("sensitive local path", request=request)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(fail_timeout),
    )
    timeout = await backend.bridge_system("health")

    def fail_connect(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("sensitive token detail", request=request)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(fail_connect),
    )
    disconnected = await backend.bridge_system("health")

    assert timeout.status is Status.TIMEOUT
    assert disconnected.status is Status.NOT_CONNECTED
    assert "sensitive" not in timeout.message
    assert "sensitive" not in disconnected.message


async def test_invalid_json_schema_and_oversized_response_are_rejected(tmp_path: Path) -> None:
    token_file, _ = write_token_file(tmp_path)
    responses = iter(
        [
            httpx.Response(200, content=b"not-json"),
            httpx.Response(200, json={"schemaVersion": "2.0"}),
            httpx.Response(200, content=b"x" * 2048),
        ]
    )
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        max_response_bytes=1024,
        transport=httpx.MockTransport(lambda request: next(responses)),
    )

    results = [
        await backend.bridge_system("health"),
        await backend.bridge_system("health"),
        await backend.bridge_system("health"),
    ]

    assert all(result.status is Status.SCHEMA_MISMATCH for result in results)
    assert all("not-json" not in result.message for result in results)


async def test_reconnect_invalidates_capability_cache(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    first_instance = uuid4()
    second_instance = uuid4()
    current_instance = first_instance

    def handle(request: httpx.Request) -> httpx.Response:
        assert request.headers["authorization"] == f"Bearer {token}"
        return httpx.Response(
            200,
            json=envelope(endpoint_data(request.url.path, instance_id=current_instance)),
        )

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )
    await backend.bridge_system("capabilities")
    assert backend.capabilities() == sorted(EXPECTED_TRUE_CAPABILITIES)

    current_instance = second_instance
    await backend.bridge_system("health")

    assert backend.capabilities() == []


async def test_bridge_doctor_passes_complete_production_handshake(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(working_handler(token)),
    )

    exit_code, report = await backend.bridge_doctor()

    assert exit_code == 0
    assert report["status"] == "OK"
    assert report["connectionState"] == "CONNECTED"
    assert all(report["checks"].values())


async def test_bridge_doctor_rejects_inconsistent_instance_and_heartbeat(tmp_path: Path) -> None:
    token_file, _ = write_token_file(tmp_path)
    first = uuid4()
    second = uuid4()
    call_count = 0

    def handle(request: httpx.Request) -> httpx.Response:
        nonlocal call_count
        call_count += 1
        instance = first if call_count == 1 else second
        return httpx.Response(
            200,
            json=envelope(
                endpoint_data(
                    request.url.path,
                    instance_id=instance,
                    heartbeat_sequence=1,
                )
            ),
        )

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )

    exit_code, report = await backend.bridge_doctor()

    assert exit_code == 1
    assert report["checks"]["sameInstanceId"] is False
    assert report["checks"]["heartbeatMonotonic"] is False


async def test_development_host_is_never_classified_as_autocad(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47779",
        token_file,
        transport=httpx.MockTransport(working_handler(token, development_host=True)),
    )

    exit_code, report = await backend.bridge_doctor()

    assert exit_code == 1
    assert report["connectionState"] == "DEVELOPMENT_HOST"
    assert report["checks"]["productionPlugin"] is False


async def test_raw_body_token_and_path_never_enter_errors(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    sensitive = f"{token} C:\\private\\bridge-token.json"
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(lambda request: httpx.Response(200, text=sensitive)),
    )

    result = await backend.bridge_system("health")
    wire = json.dumps(result.to_wire())

    assert result.status is Status.SCHEMA_MISMATCH
    assert token not in wire
    assert "private" not in wire
