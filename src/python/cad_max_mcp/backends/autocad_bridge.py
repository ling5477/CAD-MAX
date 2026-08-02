"""Authenticated local HTTP backend for the in-process AutoCAD bridge."""

from __future__ import annotations

import json
import math
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
from cad_max_mcp.models.drawing import (
    DRAWING_DATA_MODELS,
    ActiveDocumentData,
    DocumentListData,
    DrawingBoundsData,
    DrawingInspectionData,
    DrawingInspectRequest,
    DrawingLayoutsData,
    DrawingOperation,
    DrawingStatusData,
    DrawingSystemMetadataData,
    DrawingUnitsData,
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
        "drawing.status",
        "drawing.list_documents",
        "dwg.read",
    }
)
REQUIRED_FALSE_CAPABILITIES = frozenset(
    {
        "drawing.revision",
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
        """Preserve the original status operation through the strict Phase 1.4 route."""
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
        """Run one fixed drawing inspection without retrying busy/modal/timeout."""
        if deadline_ms < 100 or deadline_ms > 10_000:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.INVALID_ARGUMENT,
                message="Drawing inspection deadline is invalid",
            )
        health_envelope, health = await self._request_typed("health", BridgeHealthData)
        self._classify_connection(health_envelope, health)
        if not health_envelope.success or health is None:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=health_envelope.status,
                message=_probe_message(health_envelope.status, self._connection_state),
            )

        try:
            envelope, data = await self._request_drawing_typed(
                operation=operation,
                deadline=datetime.now(UTC) + timedelta(milliseconds=deadline_ms),
                expected_instance_id=health.instance_id,
                expected_document_id=expected_document_id,
                request_id=request_id,
                trace_id=trace_id,
            )
        except ValidationError:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.INVALID_ARGUMENT,
                message="Drawing inspection request is invalid",
            )
        if envelope.success and data is not None and data.active_document_id is not None:
            self._document_id = data.active_document_id
        elif envelope.status in {Status.DOCUMENT_DESTROYED, Status.DOCUMENT_NOT_FOUND}:
            if expected_document_id is None or expected_document_id == self._document_id:
                self._document_id = None
        return envelope

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
            "documentContentAccess": health.document_access,
            "dwgRead": health.dwg_read,
            "dwgWrite": False,
        }
        return (0 if success else 1), report

    async def drawing_doctor(
        self,
        expected_active_document_name: str | None = None,
    ) -> tuple[int, dict[str, Any]]:
        """Validate every Phase 1.4 read operation without emitting document names."""
        health_envelope, health = await self._request_typed("health", BridgeHealthData)
        capabilities_envelope, capabilities = await self._request_typed(
            "capabilities",
            BridgeCapabilitiesData,
        )
        self._classify_connection(health_envelope, health)
        if (
            not health_envelope.success
            or health is None
            or not capabilities_envelope.success
            or capabilities is None
        ):
            status = (
                health_envelope.status
                if not health_envelope.success
                else capabilities_envelope.status
            )
            return 1, self._drawing_doctor_failure(status)

        context_envelope, context = await self._request_context_typed(
            deadline=datetime.now(UTC) + timedelta(seconds=5),
            expected_instance_id=health.instance_id,
            expected_document_id=None,
        )
        if not context_envelope.success or context is None:
            return _context_exit_code(context_envelope.status), self._drawing_doctor_failure(
                context_envelope.status
            )

        results: dict[DrawingOperation, DrawingInspectionData] = {}
        for operation in DrawingOperation:
            selector = (
                None
                if operation in {DrawingOperation.STATUS, DrawingOperation.LIST_DOCUMENTS}
                else context.active_document_id
            )
            envelope, data = await self._request_drawing_typed(
                operation=operation,
                deadline=datetime.now(UTC) + timedelta(seconds=5),
                expected_instance_id=health.instance_id,
                expected_document_id=selector,
                request_id=uuid4(),
                trace_id=uuid4(),
            )
            if not envelope.success or data is None:
                return _context_exit_code(envelope.status), self._drawing_doctor_failure(
                    envelope.status
                )
            results[operation] = data

        second_envelope, second_active = await self._request_drawing_typed(
            operation=DrawingOperation.ACTIVE_DOCUMENT,
            deadline=datetime.now(UTC) + timedelta(seconds=5),
            expected_instance_id=health.instance_id,
            expected_document_id=context.active_document_id,
            request_id=uuid4(),
            trace_id=uuid4(),
        )
        if not second_envelope.success or not isinstance(second_active, ActiveDocumentData):
            return _context_exit_code(second_envelope.status), self._drawing_doctor_failure(
                second_envelope.status
            )

        drawing_status_data = cast(DrawingStatusData, results[DrawingOperation.STATUS])
        documents = cast(DocumentListData, results[DrawingOperation.LIST_DOCUMENTS])
        active = cast(ActiveDocumentData, results[DrawingOperation.ACTIVE_DOCUMENT])
        units = cast(DrawingUnitsData, results[DrawingOperation.UNITS])
        bounds = cast(DrawingBoundsData, results[DrawingOperation.BOUNDS])
        layouts = cast(DrawingLayoutsData, results[DrawingOperation.LAYOUTS])
        metadata = cast(
            DrawingSystemMetadataData,
            results[DrawingOperation.SYSTEM_METADATA],
        )
        instance_ids = {value.instance_id for value in results.values()}
        expected_true = {
            "drawing.status",
            "drawing.list_documents",
            "drawing.active_document",
            "drawing.units",
            "drawing.bounds",
            "drawing.layouts",
            "drawing.system_metadata",
            "dwg.read",
        }
        false_capabilities = {
            name for name, enabled in capabilities.capabilities.items() if not enabled
        }
        forbidden_false = REQUIRED_FALSE_CAPABILITIES.issubset(false_capabilities)
        expected_name_is_safe = (
            expected_active_document_name is None
            or _is_safe_document_display_name(expected_active_document_name)
        )
        expected_document_matches_active = expected_active_document_name is None or (
            expected_name_is_safe
            and active.display_name == expected_active_document_name
            and any(
                document.is_active
                and document.document_id == active.document_id
                and document.display_name == expected_active_document_name
                for document in documents.documents
            )
        )
        checks = {
            "sameInstanceId": instance_ids == {health.instance_id},
            "sameActiveDocumentId": all(
                value.active_document_id == context.active_document_id
                for value in results.values()
                if value.operation not in {DrawingOperation.STATUS, DrawingOperation.LIST_DOCUMENTS}
            )
            and second_active.active_document_id == context.active_document_id,
            "mainThreadVerified": all(value.main_thread_verified for value in results.values()),
            "applicationContext": (
                drawing_status_data.execution_context == "APPLICATION_CONTEXT"
                and documents.execution_context == "APPLICATION_CONTEXT"
            ),
            "documentCommandContext": all(
                value.execution_context == "DOCUMENT_COMMAND_CONTEXT"
                for value in (active, units, bounds, layouts, metadata)
            ),
            "readOnlyEvidence": all(value.read_mode == "READ_ONLY" for value in results.values()),
            "transactionTruthful": layouts.transaction_used
            and all(
                not value.transaction_used
                for value in (
                    drawing_status_data,
                    documents,
                    active,
                    units,
                    bounds,
                    metadata,
                )
            ),
            "documentCountConsistent": (
                drawing_status_data.document_count == documents.document_count
                and documents.document_count > 0
            ),
            "activeDocumentMatchesExpectedName": expected_document_matches_active,
            "capabilityHonesty": (
                all(capabilities.capabilities.get(name, False) for name in expected_true)
                and forbidden_false
            ),
            "writeScriptObjectCapabilitiesFalse": forbidden_false,
            "healthDwgRead": (
                health.document_access
                and health.dwg_read
                and not health.dwg_write
                and health.read_only
                and not health.allow_write
                and not health.allow_script
            ),
        }
        success = all(checks.values())
        if success:
            self._document_id = context.active_document_id
        report = {
            "schemaVersion": "1.0",
            "status": "OK" if success else "DRAWING_VALIDATION_FAILED",
            "connectionState": self._connection_state.value,
            "connected": self._connection_state is BridgeConnectionState.CONNECTED,
            "checks": checks,
            "instanceId": str(health.instance_id),
            "activeDocument": "REDACTED",
            "documentCount": documents.document_count,
            "layoutCount": layouts.layout_count,
            "boundsState": bounds.bounds_state,
            "insertionUnits": units.insertion_units,
            "currentSpace": metadata.current_space,
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
            "dwgRead": True,
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
        dynamic_document_capabilities = {
            "documentContext.available",
            "drawing.active_document",
            "drawing.units",
            "drawing.bounds",
            "drawing.layouts",
            "drawing.system_metadata",
        }
        fixed_true_capabilities = true_capabilities - dynamic_document_capabilities
        expected_dynamic_document_capabilities = (
            dynamic_document_capabilities
            if capabilities.capabilities.get("documentContext.available", False)
            else set()
        )
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
                fixed_true_capabilities == EXPECTED_TRUE_CAPABILITIES
                and true_capabilities & dynamic_document_capabilities
                == expected_dynamic_document_capabilities
                and false_capabilities_present
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
            "healthDocumentAccess": health.document_access,
            "healthDwgRead": health.dwg_read,
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
        report = self._doctor_report(
            "OK" if success else "BRIDGE_VALIDATION_FAILED",
            success,
            document_access=health.document_access,
            dwg_read=health.dwg_read,
        )
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
        envelope, data = await self._request_route(route, model_type)
        if data is not None:
            self._observe_instance(data)
        return envelope, data

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
            if data.instance_id != expected_instance_id or (
                expected_document_id is not None and data.active_document_id != expected_document_id
            ):
                return self._response_binding_failure(request.request_id, request.trace_id)
            self._observe_instance(data)
        elif envelope.status in {Status.DOCUMENT_DESTROYED, Status.DOCUMENT_NOT_FOUND}:
            if expected_document_id is None or expected_document_id == self._document_id:
                self._document_id = None
        return envelope, data

    async def _request_drawing_typed(
        self,
        *,
        operation: DrawingOperation,
        deadline: datetime,
        expected_instance_id: UUID,
        expected_document_id: str | None,
        request_id: UUID,
        trace_id: UUID,
    ) -> tuple[ResultEnvelope, DrawingInspectionData | None]:
        request = DrawingInspectRequest(
            request_id=request_id,
            trace_id=trace_id,
            deadline_utc=deadline,
            expected_instance_id=expected_instance_id,
            operation=operation,
            expected_document_id=expected_document_id,
        )
        model_type = cast(type[DrawingInspectionData], DRAWING_DATA_MODELS[operation])
        envelope, data = await self._request_route(
            "/v1/drawing/inspect",
            model_type,
            method="POST",
            json_body=request.model_dump(mode="json", by_alias=True),
        )
        if envelope.request_id != request.request_id or envelope.trace_id != request.trace_id:
            self._connection_state = BridgeConnectionState.INCOMPATIBLE
            return (
                ResultEnvelope.failure(
                    request_id=request.request_id,
                    trace_id=request.trace_id,
                    status=Status.SCHEMA_MISMATCH,
                    message="AutoCAD bridge returned an incompatible response",
                ),
                None,
            )
        if data is not None:
            if data.instance_id != expected_instance_id or (
                expected_document_id is not None and data.active_document_id != expected_document_id
            ):
                return self._response_binding_failure(request.request_id, request.trace_id)
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
                trust_env=False,
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
            payload: Any = json.loads(
                body,
                parse_constant=_reject_non_finite_json_constant,
                parse_float=_parse_finite_json_float,
            )
            envelope = ResultEnvelope.model_validate(payload)
            if (http_status == 200) != envelope.success:
                return self._schema_failure(started)
            if not envelope.success:
                self._classify_failure(envelope.status)
                return _sanitized_bridge_failure(envelope, started), None
            data = model_type.model_validate(envelope.data)
        except (
            UnicodeDecodeError,
            json.JSONDecodeError,
            RecursionError,
            ValidationError,
            ValueError,
        ):
            return self._schema_failure(started)

        return (
            ResultEnvelope.ok(
                request_id=envelope.request_id,
                trace_id=envelope.trace_id,
                message="AutoCAD bridge request completed",
                data=data.model_dump(mode="json", by_alias=True),
                duration_ms=max(envelope.duration_ms, _elapsed_ms(started)),
            ),
            data,
        )

    def _response_binding_failure(
        self,
        request_id: UUID,
        trace_id: UUID,
    ) -> tuple[ResultEnvelope, None]:
        """Reject a typed response before it can change instance or document state."""
        self._connection_state = BridgeConnectionState.INCOMPATIBLE
        return (
            ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.SCHEMA_MISMATCH,
                message="AutoCAD bridge returned an incompatible response",
            ),
            None,
        )

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

    def _doctor_report(
        self,
        status: str,
        connected: bool,
        *,
        document_access: bool = False,
        dwg_read: bool = False,
    ) -> dict[str, Any]:
        return {
            "schemaVersion": "1.0",
            "status": status,
            "connectionState": self._connection_state.value,
            "connected": connected,
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
            "documentAccess": document_access,
            "dwgRead": dwg_read,
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

    def _drawing_doctor_failure(self, status: Status) -> dict[str, Any]:
        return {
            "schemaVersion": "1.0",
            "status": status.value,
            "connectionState": self._connection_state.value,
            "connected": self._connection_state is BridgeConnectionState.CONNECTED,
            "activeDocument": "REDACTED",
            "readOnly": True,
            "allowWrite": False,
            "allowScript": False,
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


def _sanitized_bridge_failure(envelope: ResultEnvelope, started: float) -> ResultEnvelope:
    """Drop bridge-owned diagnostics before a failure can cross the MCP boundary."""
    return ResultEnvelope.failure(
        request_id=envelope.request_id,
        trace_id=envelope.trace_id,
        status=envelope.status,
        message=_safe_bridge_failure_message(envelope.status),
        duration_ms=max(envelope.duration_ms, _elapsed_ms(started)),
    )


def _safe_bridge_failure_message(status: Status) -> str:
    """Return a client-owned failure summary with no bridge-provided diagnostic text."""
    if status is Status.UNAUTHORIZED:
        return "AutoCAD bridge authorization failed"
    if status in {Status.NOT_CONNECTED, Status.BACKEND_UNAVAILABLE}:
        return "AutoCAD bridge is not connected"
    if status in {Status.SCHEMA_MISMATCH, Status.ROUTE_NOT_FOUND}:
        return "AutoCAD bridge response is unavailable"
    return "AutoCAD bridge request failed"


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


def _reject_non_finite_json_constant(value: str) -> None:
    """Reject JSON extensions such as NaN and Infinity before model validation."""
    del value
    raise ValueError("non-finite JSON number")


def _parse_finite_json_float(value: str) -> float:
    """Reject numeric literals that overflow to infinity during JSON parsing."""
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError("non-finite JSON number")
    return parsed


def _is_safe_document_display_name(value: str) -> bool:
    """Accept only a basename that could already appear in a redacted drawing response."""
    return (
        0 < len(value) <= 128
        and value not in {".", ".."}
        and not any(character in value for character in ("/", "\\", ":"))
        and not any(ord(character) < 32 or ord(character) == 127 for character in value)
    )


def _context_exit_code(status: Status) -> int:
    return {
        Status.NO_ACTIVE_DOCUMENT: 3,
        Status.APPLICATION_MODAL: 4,
        Status.DOCUMENT_BUSY: 5,
        Status.TIMEOUT: 6,
        Status.CANCELLED: 7,
    }.get(status, 1)
