"""Authenticated Python bridge behavior without a real AutoCAD installation."""

from __future__ import annotations

import json
import secrets
from collections.abc import Callable
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any, cast
from uuid import UUID, uuid4

import httpx
import pytest

from cad_max_mcp.backends import AutoCadBridgeBackend, NullCadBackend
from cad_max_mcp.backends.autocad_bridge import (
    EXPECTED_TRUE_CAPABILITIES,
    REQUIRED_FALSE_CAPABILITIES,
)
from cad_max_mcp.models import BridgeConnectionState, Status
from cad_max_mcp.models.drawing import DrawingOperation
from token_file_helpers import write_secure_token_file


def write_token_file(tmp_path: Path, token: str | None = None) -> tuple[Path, str]:
    """Create a per-test random token fixture."""
    encoded = token or secrets.token_urlsafe(32)
    path = tmp_path / "bridge-token.json"
    write_secure_token_file(
        path,
        {
            "schemaVersion": "1.0",
            "token": encoded,
            "createdAtUtc": "2026-07-19T00:00:00Z",
        },
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
            "documentAccess": not development_host,
            "dwgRead": not development_host,
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
        true_capabilities = {name: not development_host for name in EXPECTED_TRUE_CAPABILITIES}
        true_capabilities.update(
            {
                "bridge.health": True,
                "bridge.version": True,
                "bridge.capabilities": True,
                "bridge.heartbeat": True,
            }
        )
        return {
            **common,
            "capabilityRevision": revision,
            "pluginState": "READY",
            "capabilities": {
                **true_capabilities,
                "documentContext.available": not development_host,
                "drawing.active_document": not development_host,
                "drawing.units": not development_host,
                "drawing.bounds": not development_host,
                "drawing.layouts": not development_host,
                "drawing.system_metadata": not development_host,
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
            "contextDispatcherState": "NOT_READY" if development_host else "READY",
            "queueDepth": 0,
            "inFlightCount": 0,
            "modal": False,
            "hasActiveDocument": not development_host,
            "lastDispatchStatus": "NONE",
        }
    if path == "/v1/context/probe":
        return {
            "instanceId": str(instance_id),
            "dispatchId": str(uuid4()),
            "mainThreadVerified": True,
            "executionContext": "DOCUMENT_COMMAND_CONTEXT",
            "documentState": "ACTIVE",
            "activeDocumentId": "doc_AAAAAAAAAAAAAAAAAAAAAA",
            "isQuiescent": True,
            "queueDelayMs": 1,
            "executionMs": 1,
        }
    raise AssertionError("unexpected route")


def drawing_data(payload: dict[str, Any], *, instance_id: UUID) -> dict[str, Any]:
    operation = payload["operation"]
    document_id = "doc_AAAAAAAAAAAAAAAAAAAAAA"
    application_scope = operation in {"status", "list_documents"}
    common = {
        "instanceId": str(instance_id),
        "dispatchId": str(uuid4()),
        "operation": operation,
        "mainThreadVerified": True,
        "executionContext": (
            "APPLICATION_CONTEXT" if application_scope else "DOCUMENT_COMMAND_CONTEXT"
        ),
        "activeDocumentId": document_id,
        "queueDelayMs": 1,
        "executionMs": 1,
        "readMode": "READ_ONLY",
        "transactionUsed": operation == "layouts",
    }
    if operation == "status":
        return {
            **common,
            "runtimeState": "READY",
            "documentState": "ACTIVE",
            "documentCount": 1,
        }
    if operation == "list_documents":
        return {
            **common,
            "documents": [
                {
                    "documentId": document_id,
                    "displayName": "fixture.dwg",
                    "isUntitled": False,
                    "isActive": True,
                    "contextAvailable": True,
                }
            ],
            "documentCount": 1,
        }
    if operation == "active_document":
        return {
            **common,
            "documentId": document_id,
            "displayName": "fixture.dwg",
            "isUntitled": False,
            "isQuiescent": True,
            "documentState": "ACTIVE",
        }
    if operation == "units":
        return {
            **common,
            "insertionUnits": "MILLIMETERS",
            "linearFormat": "DECIMAL",
            "linearPrecision": 4,
            "angularFormat": "DECIMAL_DEGREES",
            "angularPrecision": 2,
            "unitless": False,
            "millimetersPerDrawingUnit": 1.0,
        }
    if operation == "bounds":
        return {
            **common,
            "boundsState": "EMPTY",
            "source": "DATABASE_EXTENTS",
            "coordinateSystem": "WCS",
            "minimum": None,
            "maximum": None,
            "size": None,
            "extentsMayBeStale": True,
        }
    if operation == "layouts":
        return {
            **common,
            "layouts": [{"name": "Model", "isModel": True, "tabOrder": 0, "isCurrent": True}],
            "layoutCount": 1,
        }
    if operation == "system_metadata":
        return {
            **common,
            "fileBacked": True,
            "fileFormatVersion": "ACAD2018",
            "tileMode": True,
            "currentSpace": "MODEL_SPACE",
            "currentLayoutName": "Model",
        }
    raise AssertionError("unexpected drawing operation")


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
        response_envelope = envelope(
            drawing_data(json.loads(request.content), instance_id=current_instance)
            if request.url.path == "/v1/drawing/inspect"
            else endpoint_data(
                request.url.path,
                instance_id=current_instance,
                heartbeat_sequence=max(1, heartbeat),
                development_host=development_host,
            )
        )
        if request.url.path in {"/v1/context/probe", "/v1/drawing/inspect"}:
            request_payload = json.loads(request.content)
            response_envelope["requestId"] = request_payload["requestId"]
            response_envelope["traceId"] = request_payload["traceId"]
        return httpx.Response(200, json=response_envelope)

    return handle


def working_handler_with_capability_inventory(
    token: str,
    capability_inventory: dict[str, bool],
) -> Callable[[httpx.Request], httpx.Response]:
    """Serve a complete bridge response with a deliberately chosen capability inventory."""
    base_handler = working_handler(token)

    def handle(request: httpx.Request) -> httpx.Response:
        response = base_handler(request)
        if request.url.path != "/v1/capabilities":
            return response

        payload = response.json()
        payload["data"]["capabilities"] = capability_inventory
        return httpx.Response(response.status_code, json=payload)

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
    assert backend.capabilities() == sorted(
        EXPECTED_TRUE_CAPABILITIES
        | {
            "documentContext.available",
            "drawing.active_document",
            "drawing.units",
            "drawing.bounds",
            "drawing.layouts",
            "drawing.system_metadata",
        }
    )

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


async def test_bridge_doctor_does_not_overclaim_health_document_capabilities(
    tmp_path: Path,
) -> None:
    token_file, token = write_token_file(tmp_path)
    base_handler = working_handler(token)

    def handle(request: httpx.Request) -> httpx.Response:
        response = base_handler(request)
        if request.url.path != "/v1/health":
            return response

        payload = response.json()
        payload["data"]["documentAccess"] = False
        payload["data"]["dwgRead"] = False
        return httpx.Response(response.status_code, json=payload)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )

    exit_code, report = await backend.bridge_doctor()

    assert exit_code == 1
    assert report["checks"]["healthDocumentAccess"] is False
    assert report["checks"]["healthDwgRead"] is False
    assert report["documentAccess"] is False
    assert report["dwgRead"] is False


async def test_context_probe_and_doctor_verify_stable_document_context(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    instance_id = uuid4()
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(working_handler(token, instance_id=instance_id)),
    )

    probe = await backend.context_probe(
        deadline=datetime.now(UTC) + timedelta(seconds=5),
        expected_instance_id=instance_id,
    )
    exit_code, report = await backend.context_doctor()

    assert probe.success is True
    assert exit_code == 0
    assert report["status"] == "OK"
    assert report["checks"]["mainThreadVerified"] is True
    assert report["checks"]["stableActiveDocumentId"] is True
    assert report["documentContentAccess"] is True


async def test_drawing_operations_and_doctor_are_strict_and_redacted(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(working_handler(token)),
    )

    for operation in DrawingOperation:
        result = await backend.drawing_inspect(
            operation,
            expected_document_id=(
                None
                if operation in {DrawingOperation.STATUS, DrawingOperation.LIST_DOCUMENTS}
                else "doc_AAAAAAAAAAAAAAAAAAAAAA"
            ),
            deadline_ms=5000,
            request_id=uuid4(),
            trace_id=uuid4(),
        )
        assert result.success is True

    exit_code, report = await backend.drawing_doctor(expected_active_document_name="fixture.dwg")
    serialized = json.dumps(report)
    assert exit_code == 0
    assert all(report["checks"].values())
    assert "fixture.dwg" not in serialized
    assert "CADMAX_SECRET_DIRECTORY_SENTINEL" not in serialized


async def test_drawing_doctor_rejects_unmatched_or_unsafe_expected_active_name(
    tmp_path: Path,
) -> None:
    token_file, token = write_token_file(tmp_path)
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(working_handler(token)),
    )

    exit_code, report = await backend.drawing_doctor(
        expected_active_document_name=r"C:\\CADMAX_SECRET_DIRECTORY_SENTINEL\\fixture.dwg"
    )
    serialized = json.dumps(report)

    assert exit_code == 1
    assert report["checks"]["activeDocumentMatchesExpectedName"] is False
    assert "CADMAX_SECRET_DIRECTORY_SENTINEL" not in serialized


async def test_drawing_doctor_requires_complete_false_capability_inventory(
    tmp_path: Path,
) -> None:
    token_file, token = write_token_file(tmp_path)
    complete_inventory = cast(
        dict[str, bool],
        endpoint_data("/v1/capabilities", instance_id=uuid4())["capabilities"],
    )
    incomplete_one = {
        name: enabled for name, enabled in complete_inventory.items() if name != "dwg.write"
    }
    incomplete_all = {
        name: enabled
        for name, enabled in complete_inventory.items()
        if name not in REQUIRED_FALSE_CAPABILITIES
    }
    explicitly_enabled = {**complete_inventory, "dwg.write": True}

    for inventory, expected_exit_code in (
        (complete_inventory, 0),
        (incomplete_one, 1),
        (incomplete_all, 1),
        (explicitly_enabled, 1),
    ):
        backend = AutoCadBridgeBackend(
            "http://127.0.0.1:47770",
            token_file,
            transport=httpx.MockTransport(
                working_handler_with_capability_inventory(token, inventory)
            ),
        )

        exit_code, report = await backend.drawing_doctor()

        assert exit_code == expected_exit_code
        assert report["checks"]["capabilityHonesty"] is (expected_exit_code == 0)
        assert report["checks"]["writeScriptObjectCapabilitiesFalse"] is (expected_exit_code == 0)


async def test_drawing_rejects_path_leakage_and_does_not_retry_busy(tmp_path: Path) -> None:
    token_file, token = write_token_file(tmp_path)
    instance_id = uuid4()
    drawing_calls = 0

    def handle(request: httpx.Request) -> httpx.Response:
        nonlocal drawing_calls
        assert request.headers["authorization"] == f"Bearer {token}"
        if request.url.path == "/v1/health":
            return httpx.Response(
                200,
                json=envelope(endpoint_data(request.url.path, instance_id=instance_id)),
            )
        drawing_calls += 1
        payload = json.loads(request.content)
        if drawing_calls == 1:
            response = error_envelope("DOCUMENT_BUSY")
            response["requestId"] = payload["requestId"]
            response["traceId"] = payload["traceId"]
            return httpx.Response(409, json=response)
        data = drawing_data(payload, instance_id=instance_id)
        data["displayName"] = r"C:\CADMAX_SECRET_DIRECTORY_SENTINEL\fixture.dwg"
        response = envelope(data)
        response["requestId"] = payload["requestId"]
        response["traceId"] = payload["traceId"]
        return httpx.Response(200, json=response)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )
    busy = await backend.drawing_inspect(
        DrawingOperation.ACTIVE_DOCUMENT,
        expected_document_id=None,
        deadline_ms=5000,
        request_id=uuid4(),
        trace_id=uuid4(),
    )
    leaked = await backend.drawing_inspect(
        DrawingOperation.ACTIVE_DOCUMENT,
        expected_document_id=None,
        deadline_ms=5000,
        request_id=uuid4(),
        trace_id=uuid4(),
    )

    assert busy.status is Status.DOCUMENT_BUSY
    assert leaked.status is Status.SCHEMA_MISMATCH
    assert drawing_calls == 2


async def test_context_probe_does_not_retry_busy_and_clears_destroyed_id(
    tmp_path: Path,
) -> None:
    token_file, _ = write_token_file(tmp_path)
    instance_id = uuid4()
    calls = 0

    def handle(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        payload = json.loads(request.content)
        status = "DOCUMENT_BUSY" if calls == 1 else "DOCUMENT_NOT_FOUND"
        response = error_envelope(status)
        response["requestId"] = payload["requestId"]
        response["traceId"] = payload["traceId"]
        return httpx.Response(409, json=response)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )
    busy = await backend.context_probe(
        deadline=datetime.now(UTC) + timedelta(seconds=5),
        expected_instance_id=instance_id,
    )
    missing = await backend.context_probe(
        deadline=datetime.now(UTC) + timedelta(seconds=5),
        expected_instance_id=instance_id,
        expected_document_id="doc_AAAAAAAAAAAAAAAAAAAAAA",
    )

    assert busy.status is Status.DOCUMENT_BUSY
    assert missing.status is Status.DOCUMENT_NOT_FOUND
    assert calls == 2


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


async def test_bridge_client_disables_environment_proxy_inheritance(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    token_file, token = write_token_file(tmp_path)
    original_client = httpx.AsyncClient
    observed_trust_env: list[object] = []

    class RecordingAsyncClient(original_client):
        def __init__(self, *args: object, **kwargs: object) -> None:
            observed_trust_env.append(kwargs.get("trust_env"))
            super().__init__(*args, **kwargs)  # type: ignore[arg-type]

    monkeypatch.setattr(
        "cad_max_mcp.backends.autocad_bridge.httpx.AsyncClient",
        RecordingAsyncClient,
    )
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(working_handler(token)),
    )

    result = await backend.bridge_system("health")

    assert result.success is True
    assert observed_trust_env == [False]


async def test_typed_bridge_data_replaces_untrusted_wire_shape(tmp_path: Path) -> None:
    token_file, _ = write_token_file(tmp_path)

    def handle(request: httpx.Request) -> httpx.Response:
        data = endpoint_data(request.url.path, instance_id=uuid4())
        if request.url.path == "/v1/version":
            data["autocadYear"] = "2025"
        return httpx.Response(200, json=envelope(data))

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )

    result = await backend.bridge_system("version")

    assert result.success is True
    assert result.data["autocadYear"] == 2025


async def test_bridge_rejects_nonfinite_json_numbers_before_schema_validation(
    tmp_path: Path,
) -> None:
    token_file, _ = write_token_file(tmp_path)
    instance_id = uuid4()

    def handle(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/health":
            return httpx.Response(
                200,
                json=envelope(endpoint_data("/v1/health", instance_id=instance_id)),
            )
        request_data = json.loads(request.content)
        data = drawing_data(request_data, instance_id=instance_id)
        data.update(
            {
                "boundsState": "AVAILABLE",
                "minimum": {"x": float("nan"), "y": 0, "z": 0},
                "maximum": {"x": 1, "y": 1, "z": 1},
                "size": {"x": 1, "y": 1, "z": 1},
            }
        )
        response = envelope(data)
        response["requestId"] = request_data["requestId"]
        response["traceId"] = request_data["traceId"]
        return httpx.Response(200, content=json.dumps(response).encode("utf-8"))

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )

    result = await backend.drawing_inspect(
        DrawingOperation.BOUNDS,
        expected_document_id=None,
        deadline_ms=5000,
        request_id=uuid4(),
        trace_id=uuid4(),
    )

    assert result.status is Status.SCHEMA_MISMATCH


async def test_bridge_maps_byte_bounded_deep_json_to_schema_failure(tmp_path: Path) -> None:
    """A parser-depth failure must not escape the client-safe response boundary."""
    token_file, _ = write_token_file(tmp_path)
    nested_depth = 1200
    response = (
        b'{"schemaVersion":"1.0","requestId":"'
        + str(uuid4()).encode("ascii")
        + b'","traceId":"'
        + str(uuid4()).encode("ascii")
        + b'","success":true,"status":"OK","errorCode":null,'
        + b'"message":"untrusted","data":'
        + b"[" * nested_depth
        + b"0"
        + b"]" * nested_depth
        + b',"warnings":[],"durationMs":0}'
    )
    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(lambda request: httpx.Response(200, content=response)),
    )

    result = await backend.bridge_system("health")

    assert result.status is Status.SCHEMA_MISMATCH
    assert result.data == {}


async def test_bridge_redacts_success_and_failure_diagnostics_before_mcp_projection(
    tmp_path: Path,
) -> None:
    """Bridge-owned text and maps must never survive the typed client boundary."""
    token_file, token = write_token_file(tmp_path)
    sensitive = f"{token} C:\\CADMAX_SECRET_DIRECTORY_SENTINEL"
    instance_id = uuid4()

    def success_handler(request: httpx.Request) -> httpx.Response:
        response = envelope(endpoint_data(request.url.path, instance_id=instance_id))
        response["message"] = sensitive
        response["warnings"] = [sensitive]
        return httpx.Response(200, json=response)

    success_backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(success_handler),
    )
    success = await success_backend.bridge_system("health")

    def failure_handler(request: httpx.Request) -> httpx.Response:
        response = error_envelope("AUTOCAD_API_ERROR")
        response["message"] = sensitive
        response["warnings"] = [sensitive]
        response["data"] = {"diagnostic": sensitive}
        return httpx.Response(500, json=response)

    failure_backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(failure_handler),
    )
    failure = await failure_backend.bridge_system("health")

    for result in (success, failure):
        wire = json.dumps(result.to_wire())
        assert token not in wire
        assert "CADMAX_SECRET_DIRECTORY_SENTINEL" not in wire
        assert result.warnings == []
    assert success.message == "AutoCAD bridge request completed"
    assert failure.message == "AutoCAD bridge request failed"
    assert failure.data == {}


@pytest.mark.parametrize("mismatch", ["instance", "document"])
async def test_drawing_response_identity_must_bind_to_requested_context(
    tmp_path: Path,
    mismatch: str,
) -> None:
    """A correctly correlated response still fails if its instance or document differs."""
    token_file, token = write_token_file(tmp_path)
    expected_instance = uuid4()
    unexpected_instance = uuid4()

    def handle(request: httpx.Request) -> httpx.Response:
        assert request.headers["authorization"] == f"Bearer {token}"
        if request.url.path == "/v1/health":
            return httpx.Response(
                200,
                json=envelope(endpoint_data("/v1/health", instance_id=expected_instance)),
            )
        request_data = json.loads(request.content)
        data = drawing_data(
            request_data,
            instance_id=unexpected_instance if mismatch == "instance" else expected_instance,
        )
        if mismatch == "document":
            data["activeDocumentId"] = "doc_BBBBBBBBBBBBBBBBBBBBBB"
            data["documentId"] = "doc_BBBBBBBBBBBBBBBBBBBBBB"
        response = envelope(data)
        response["requestId"] = request_data["requestId"]
        response["traceId"] = request_data["traceId"]
        return httpx.Response(200, json=response)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        token_file,
        transport=httpx.MockTransport(handle),
    )

    result = await backend.drawing_inspect(
        DrawingOperation.ACTIVE_DOCUMENT,
        expected_document_id="doc_AAAAAAAAAAAAAAAAAAAAAA",
        deadline_ms=5000,
        request_id=uuid4(),
        trace_id=uuid4(),
    )

    assert result.status is Status.SCHEMA_MISMATCH
    assert backend.connection_state is BridgeConnectionState.INCOMPATIBLE
