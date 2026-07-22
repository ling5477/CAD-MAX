"""Authenticated local HTTP backend for the in-process AutoCAD bridge."""

from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta
from pathlib import Path
from time import perf_counter
from typing import Any, TypeVar, cast
from uuid import UUID, uuid4

import httpx
from pydantic import ValidationError

from cad_max_mcp.backends.base import BackendProbe
from cad_max_mcp.models.bridge import (
    BridgeCapabilitiesData,
    BridgeConnectionState,
    BridgeHealthData,
    BridgeHeartbeatData,
    BridgeInstanceModel,
    BridgeModel,
    BridgePluginState,
    BridgeVersionData,
    ContextProbeData,
    ContextProbeRequest,
)
from cad_max_mcp.models.envelope import ResultEnvelope, Status
from cad_max_mcp.security.bridge_token import BridgeTokenError, load_bridge_token

BridgeData = TypeVar("BridgeData", bound=BridgeInstanceModel)

ROUTES: dict[str, tuple[str, type[BridgeInstanceModel]]] = {
    "health": ("/v1/health", BridgeHealthData),
    "version": ("/v1/version", BridgeVersionData),
    "capabilities": ("/v1/capabilities", BridgeCapabilitiesData),
    "heartbeat": ("/v1/heartbeat", BridgeHeartbeatData),
}
EXPECTED_TRUE_CAPABILITIES = frozenset(
    {
        "bridge.health",
        "bridge.version",
        "bridge.capabilities",
        "bridge.heartbeat",
        "bridge.contextDispatch",
        "bridge.contextProbe",
    }
)
REQUIRED_FALSE_CAPABILITIES = frozenset(
    {
        "drawing.active_document",
        "drawing.list_documents",
        "drawing.units",
        "drawing.bounds",
        "drawing.layouts",
        "query.entity_count",
        "query.count_by_type",
        "query.list_entities",
        "query.entity_summary",
        "query.resolve_handle",
        "query.measure_geometry",
        "layer.list_layers",
        "block.list_definitions",
        "block.list_references",
        "block.list_attributes",
        "style.list_text_styles",
        "style.list_dimension_styles",
        "style.list_linetypes",
        "selection.get_pickfirst",
        "preview.render_pdf",
        "preview.render_png",
        "dwg.read",
        "dwg.write",
        "script",
        "command",
    }
)


class AutoCadBridgeBackend:
    """Call the authenticated loopback bridge with bounded, sanitized behavior."""

    def __init__(
        self,
        base_url: str,
        token_file: Path | None,
        *,
        connect_timeout_seconds: float = 1.0,
        read_timeout_seconds: float = 2.0,
        max_response_bytes: int = 65_536,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._token_file = token_file
        self._timeout = httpx.Timeout(
            read_timeout_seconds,
            connect=connect_timeout_seconds,
            write=read_timeout_seconds,
            pool=connect_timeout_seconds,
        )
        self._max_response_bytes = max_response_bytes
        self._transport = transport
        self._instance_id: UUID | None = None
        self._capability_revision: str | None = None
        self._capability_cache: dict[str, bool] = {}
        self._document_id: str | None = None
        self._connection_state = BridgeConnectionState.NOT_CONNECTED

    @property
    def connection_state(self) -> BridgeConnectionState:
        """Return only the latest sanitized connection classification."""
        return self._connection_state

    async def health(self) -> BackendProbe:
        """Probe real process-level health without touching a document."""
        envelope, data = await self._request_typed("health", BridgeHealthData)
        health = data
        self._classify_connection(envelope, health)
        return BackendProbe(
            configured=True,
            available=self._connection_state is BridgeConnectionState.CONNECTED,
            status=envelope.status,
            message=_probe_message(envelope.status, self._connection_state),
            connection_state=self._connection_state,
            envelope=envelope,
        )

    async def bridge_system(self, operation: str) -> ResultEnvelope:
        """Return one validated process-level endpoint envelope."""
        if operation not in ROUTES:
            return _failure(Status.NOT_IMPLEMENTED, "AutoCAD bridge operation is not implemented")
        _, model_type = ROUTES[operation]
        envelope, data = await self._request_typed(operation, model_type)
        if isinstance(data, (BridgeHealthData, BridgeVersionData, BridgeCapabilitiesData)):
            self._classify_connection(envelope, data)
        elif not envelope.success:
            self._classify_failure(envelope.status)
        return envelope

    async def drawing_status(self, request_id: UUID, trace_id: UUID) -> ResultEnvelope:
        """Report connection state without claiming document or DWG access."""
        probe = await self.health()
        if not probe.envelope or not probe.envelope.success:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=probe.status,
                message=probe.message,
                data={"connectionState": probe.connection_state.value},
            )
        return ResultEnvelope.failure(
            request_id=request_id,
            trace_id=trace_id,
            status=Status.NOT_IMPLEMENTED,
            message="AutoCAD document status is not implemented",
            data={"connectionState": self._connection_state.value},
        )

    async def context_probe(
        self,
        *,
        deadline: datetime,
        expected_instance_id: UUID,
        expected_document_id: str | None = None,
    ) -> ResultEnvelope:
        """Run the sole fixed document-context probe without retrying failures."""
        try:
            envelope, data = await self._request_context_typed(
                deadline=deadline,
                expected_instance_id=expected_instance_id,
                expected_document_id=expected_document_id,
            )
        except ValidationError:
            return _failure(
                Status.INVALID_ARGUMENT,
                "AutoCAD context probe request is invalid",
            )
        if envelope.success and data is not None:
            self._document_id = data.active_document_id
        elif envelope.status in {Status.DOCUMENT_DESTROYED, Status.DOCUMENT_NOT_FOUND}:
            if expected_document_id is None or expected_document_id == self._document_id:
                self._document_id = None
        return envelope

    async def context_doctor(self) -> tuple[int, dict[str, Any]]:
        """Verify main-thread command context and stable active-document identity."""
        health_envelope, health = await self._request_typed("health", BridgeHealthData)
        self._classify_connection(health_envelope, health)
        if not health_envelope.success or health is None:
            self._classify_failure(health_envelope.status)
            return 1, self._context_doctor_failure(health_envelope.status)

        first_envelope, first = await self._request_context_typed(
            deadline=datetime.now(UTC) + timedelta(seconds=5),
            expected_instance_id=health.instance_id,
            expected_document_id=None,
        )
        if not first_envelope.success or first is None:
            return _context_exit_code(first_envelope.status), self._context_doctor_failure(
                first_envelope.status
            )

        second_envelope, second = await self._request_context_typed(
            deadline=datetime.now(UTC) + timedelta(seconds=5),
            expected_instance_id=health.instance_id,
            expected_document_id=first.active_document_id,
        )
        if not second_envelope.success or second is None:
            return _context_exit_code(second_envelope.status), self._context_doctor_failure(
                second_envelope.status
            )

        checks = {
            "mainThreadVerified": first.main_thread_verified and second.main_thread_verified,
            "documentCommandContext": (
                first.execution_context == "DOCUMENT_COMMAND_CONTEXT"
                and second.execution_context == "DOCUMENT_COMMAND_CONTEXT"
            ),
            "sameInstanceId": (first.instance_id == health.instance_id == second.instance_id),
            "stableActiveDocumentId": (first.active_document_id == second.active_document_id),
            "documentIdFormat": (
                first.active_document_id.startswith("doc_") and len(first.active_document_id) == 26
            ),
            "quiescent": first.is_quiescent and second.is_quiescent,
        }
        success = all(checks.values())
        if success:
            self._document_id = first.active_document_id
        report = {
            "schemaVersion": "1.0",
            "status": "OK" if success else "CONTEXT_VALIDATION_FAILED",
            "connectionState": self._connection_state.value,
            "connected": self._connection_state is BridgeConnectionState.CONNECTED,
            "checks": checks,
            "instanceId": str(health.instance_id),
            "activeDocumentId": first.active_document_id if success else None,
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
            "documentContentAccess": False,
            "dwgRead": False,
            "dwgWrite": False,
        }
        return (0 if success else 1), report

    def capabilities(self) -> list[str]:
        """Return only cached capabilities explicitly reported true by this instance."""
        return sorted(name for name, enabled in self._capability_cache.items() if enabled)

    async def bridge_doctor(self) -> tuple[int, dict[str, Any]]:
        """Validate all endpoints, identity, heartbeat monotonicity, and capability honesty."""
        results: list[tuple[ResultEnvelope, BridgeModel | None]] = []
        for operation, model_type in (
            ("health", BridgeHealthData),
            ("version", BridgeVersionData),
            ("capabilities", BridgeCapabilitiesData),
            ("heartbeat", BridgeHeartbeatData),
            ("heartbeat", BridgeHeartbeatData),
        ):
            results.append(await self._request_typed(operation, model_type))

        failed = next((envelope for envelope, _ in results if not envelope.success), None)
        if failed is not None or any(data is None for _, data in results):
            status = failed.status if failed is not None else Status.SCHEMA_MISMATCH
            self._classify_failure(status)
            return 1, self._doctor_report(status.value, connected=False)

        health = cast(BridgeHealthData, results[0][1])
        version = cast(BridgeVersionData, results[1][1])
        capabilities = cast(BridgeCapabilitiesData, results[2][1])
        heartbeat_one = cast(BridgeHeartbeatData, results[3][1])
        heartbeat_two = cast(BridgeHeartbeatData, results[4][1])
        instance_ids = {
            health.instance_id,
            version.instance_id,
            capabilities.instance_id,
            heartbeat_one.instance_id,
            heartbeat_two.instance_id,
        }
        revisions = {
            version.capability_revision,
            capabilities.capability_revision,
            heartbeat_one.capability_revision,
            heartbeat_two.capability_revision,
        }
        true_capabilities = {name for name, enabled in capabilities.capabilities.items() if enabled}
        fixed_true_capabilities = true_capabilities - {"documentContext.available"}
        false_capabilities_present = REQUIRED_FALSE_CAPABILITIES.issubset(
            {name for name, enabled in capabilities.capabilities.items() if not enabled}
        )
        checks = {
            "sameInstanceId": len(instance_ids) == 1,
            "sameCapabilityRevision": len(revisions) == 1,
            "heartbeatMonotonic": (
                heartbeat_two.heartbeat_sequence > heartbeat_one.heartbeat_sequence
            ),
            "capabilityHonesty": (
                fixed_true_capabilities == EXPECTED_TRUE_CAPABILITIES and false_capabilities_present
            ),
            "pluginReady": all(
                state is BridgePluginState.READY
                for state in (
                    health.plugin_state,
                    capabilities.plugin_state,
                    heartbeat_one.plugin_state,
                    heartbeat_two.plugin_state,
                )
            ),
            "autocadConnected": all(
                (
                    health.autocad_connected,
                    version.autocad_connected,
                    capabilities.autocad_connected,
                    heartbeat_one.autocad_connected,
                    heartbeat_two.autocad_connected,
                )
            ),
            "productionPlugin": not any(
                (
                    health.development_host,
                    version.development_host,
                    capabilities.development_host,
                    heartbeat_one.development_host,
                    heartbeat_two.development_host,
                )
            ),
        }
        success = all(checks.values())
        self._connection_state = (
            BridgeConnectionState.CONNECTED
            if success
            else (
                BridgeConnectionState.DEVELOPMENT_HOST
                if not checks["productionPlugin"]
                else BridgeConnectionState.INCOMPATIBLE
            )
        )
        report = self._doctor_report("OK" if success else "BRIDGE_VALIDATION_FAILED", success)
        report["checks"] = checks
        if success:
            report["instanceId"] = str(health.instance_id)
            report["capabilityRevision"] = capabilities.capability_revision
            report["autoCADYear"] = version.autocad_year
        return (0 if success else 1), report

    async def _request_typed(
        self,
        operation: str,
        model_type: type[BridgeData],
    ) -> tuple[ResultEnvelope, BridgeData | None]:
        route, _ = ROUTES[operation]
        return await self._request_route(route, model_type)

    async def _request_context_typed(
        self,
        *,
        deadline: datetime,
        expected_instance_id: UUID,
        expected_document_id: str | None,
    ) -> tuple[ResultEnvelope, ContextProbeData | None]:
        request = ContextProbeRequest(
            request_id=uuid4(),
            trace_id=uuid4(),
            deadline_utc=deadline,
            expected_instance_id=expected_instance_id,
            expected_document_id=expected_document_id,
        )
        envelope, data = await self._request_route(
            "/v1/context/probe",
            ContextProbeData,
            method="POST",
            json_body=request.model_dump(mode="json", by_alias=True),
        )
        if envelope.request_id != request.request_id or envelope.trace_id != request.trace_id:
            self._connection_state = BridgeConnectionState.INCOMPATIBLE
            return (
                _failure(
                    Status.SCHEMA_MISMATCH,
                    "AutoCAD bridge returned an incompatible response",
                ),
                None,
            )
        if data is not None:
            self._observe_instance(data)
        elif envelope.status in {Status.DOCUMENT_DESTROYED, Status.DOCUMENT_NOT_FOUND}:
            if expected_document_id is None or expected_document_id == self._document_id:
                self._document_id = None
        return envelope, data

    async def _request_route(
        self,
        route: str,
        model_type: type[BridgeData],
        *,
        method: str = "GET",
        json_body: dict[str, Any] | None = None,
    ) -> tuple[ResultEnvelope, BridgeData | None]:
        started = perf_counter()
        try:
            token = load_bridge_token(self._token_file)
        except BridgeTokenError as error:
            status = Status(error.error_code)
            self._classify_failure(status)
            return _failure(status, _token_error_message(status), started), None

        try:
            async with httpx.AsyncClient(
                timeout=self._timeout,
                transport=self._transport,
                follow_redirects=False,
            ) as client:
                async with client.stream(
                    method,
                    f"{self._base_url}{route}",
                    headers={"Authorization": token.authorization_header},
                    json=json_body,
                ) as response:
                    content_length = response.headers.get("content-length")
                    if content_length is not None:
                        try:
                            declared_length = int(content_length)
                        except ValueError:
                            return self._schema_failure(started)
                        if declared_length < 0 or declared_length > self._max_response_bytes:
                            return self._schema_failure(started)

                    body = bytearray()
                    async for chunk in response.aiter_bytes():
                        body.extend(chunk)
                        if len(body) > self._max_response_bytes:
                            return self._schema_failure(started)
                    http_status = response.status_code
        except httpx.TimeoutException:
            self._connection_state = BridgeConnectionState.NOT_CONNECTED
            return _failure(Status.TIMEOUT, "AutoCAD bridge request timed out", started), None
        except httpx.HTTPError:
            self._connection_state = BridgeConnectionState.NOT_CONNECTED
            return _failure(Status.NOT_CONNECTED, "AutoCAD bridge is not connected", started), None

        try:
            payload: Any = json.loads(body)
            envelope = ResultEnvelope.model_validate(payload)
            if (http_status == 200) != envelope.success:
                return self._schema_failure(started)
            if not envelope.success:
                self._classify_failure(envelope.status)
                return envelope, None
            data = model_type.model_validate(envelope.data)
        except (UnicodeDecodeError, json.JSONDecodeError, ValidationError, ValueError):
            return self._schema_failure(started)

        self._observe_instance(data)
        return envelope.model_copy(
            update={"duration_ms": max(envelope.duration_ms, _elapsed_ms(started))}
        ), data

    def _observe_instance(self, data: BridgeInstanceModel) -> None:
        instance_id = data.instance_id
        if self._instance_id is not None and self._instance_id != instance_id:
            self._capability_cache.clear()
            self._capability_revision = None
            self._document_id = None
        self._instance_id = instance_id

        revision = (
            data.capability_revision
            if isinstance(data, (BridgeVersionData, BridgeCapabilitiesData, BridgeHeartbeatData))
            else None
        )
        if revision is not None and self._capability_revision != revision:
            self._capability_cache.clear()
            self._capability_revision = revision
        if isinstance(data, BridgeCapabilitiesData):
            self._capability_cache = dict(data.capabilities)

    def _classify_connection(
        self,
        envelope: ResultEnvelope,
        data: BridgeHealthData | BridgeVersionData | BridgeCapabilitiesData | None,
    ) -> None:
        if not envelope.success or data is None:
            self._classify_failure(envelope.status)
        elif data.development_host:
            self._connection_state = BridgeConnectionState.DEVELOPMENT_HOST
        elif data.autocad_connected:
            self._connection_state = BridgeConnectionState.CONNECTED
        else:
            self._connection_state = BridgeConnectionState.NOT_CONNECTED

    def _classify_failure(self, status: Status) -> None:
        if status is Status.UNAUTHORIZED:
            self._connection_state = BridgeConnectionState.UNAUTHORIZED
        elif status in {
            Status.SCHEMA_MISMATCH,
            Status.ROUTE_NOT_FOUND,
        }:
            self._connection_state = BridgeConnectionState.INCOMPATIBLE
        elif status in {
            Status.TOKEN_NOT_CONFIGURED,
            Status.TOKEN_CONFIG_INVALID,
            Status.TOKEN_FILE_INSECURE,
            Status.TOKEN_INVALID,
            Status.TOKEN_UNAVAILABLE,
            Status.BACKEND_NOT_CONFIGURED,
        }:
            self._connection_state = BridgeConnectionState.NOT_CONFIGURED
        else:
            self._connection_state = BridgeConnectionState.NOT_CONNECTED

    def _schema_failure(
        self,
        started: float,
    ) -> tuple[ResultEnvelope, None]:
        self._connection_state = BridgeConnectionState.INCOMPATIBLE
        return (
            _failure(
                Status.SCHEMA_MISMATCH,
                "AutoCAD bridge returned an incompatible response",
                started,
            ),
            None,
        )

    def _doctor_report(self, status: str, connected: bool) -> dict[str, Any]:
        return {
            "schemaVersion": "1.0",
            "status": status,
            "connectionState": self._connection_state.value,
            "connected": connected,
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
            "documentAccess": False,
            "dwgRead": False,
            "dwgWrite": False,
        }

    def _context_doctor_failure(self, status: Status) -> dict[str, Any]:
        return {
            "schemaVersion": "1.0",
            "status": status.value,
            "connectionState": self._connection_state.value,
            "connected": self._connection_state is BridgeConnectionState.CONNECTED,
            "mainThreadVerified": False,
            "documentCommandContext": False,
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
            "documentContentAccess": False,
            "dwgRead": False,
            "dwgWrite": False,
        }


def _failure(status: Status, message: str, started: float | None = None) -> ResultEnvelope:
    return ResultEnvelope.failure(
        request_id=uuid4(),
        trace_id=uuid4(),
        status=status,
        message=message,
        duration_ms=0 if started is None else _elapsed_ms(started),
    )


def _token_error_message(status: Status) -> str:
    return {
        Status.TOKEN_NOT_CONFIGURED: "AutoCAD bridge token is not configured",
        Status.TOKEN_CONFIG_INVALID: "AutoCAD bridge token configuration is invalid",
        Status.TOKEN_FILE_INSECURE: "AutoCAD bridge token file is insecure",
        Status.TOKEN_INVALID: "AutoCAD bridge token is invalid",
        Status.TOKEN_UNAVAILABLE: "AutoCAD bridge token is unavailable",
    }.get(status, "AutoCAD bridge token configuration failed")


def _probe_message(status: Status, connection_state: BridgeConnectionState) -> str:
    if connection_state is BridgeConnectionState.CONNECTED:
        return "AutoCAD bridge is connected"
    if connection_state is BridgeConnectionState.DEVELOPMENT_HOST:
        return "Development bridge Host is not AutoCAD"
    if status is Status.UNAUTHORIZED:
        return "AutoCAD bridge authorization failed"
    if connection_state is BridgeConnectionState.NOT_CONFIGURED:
        return "AutoCAD bridge is not configured"
    return "AutoCAD bridge is not connected"


def _elapsed_ms(started: float) -> int:
    return max(0, int((perf_counter() - started) * 1000))


def _context_exit_code(status: Status) -> int:
    return {
        Status.NO_ACTIVE_DOCUMENT: 3,
        Status.APPLICATION_MODAL: 4,
        Status.DOCUMENT_BUSY: 5,
        Status.TIMEOUT: 6,
        Status.CANCELLED: 7,
    }.get(status, 1)
