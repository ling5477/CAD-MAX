"""Wire-contract serialization tests."""

from __future__ import annotations

import json
from pathlib import Path
from uuid import uuid4

from cad_max_mcp.models import ResultEnvelope, Status
from cad_max_mcp.models.bridge import BridgeHealthData, BridgePluginState


def test_envelope_serializes_with_camel_case() -> None:
    """The Python envelope matches the language-neutral JSON contract."""
    request_id = uuid4()
    trace_id = uuid4()

    wire = ResultEnvelope.ok(
        request_id=request_id,
        trace_id=trace_id,
        message="healthy",
        data={"bridgeConfigured": False},
    ).to_wire()

    assert wire == {
        "schemaVersion": "1.0",
        "requestId": str(request_id),
        "traceId": str(trace_id),
        "success": True,
        "status": "OK",
        "errorCode": None,
        "message": "healthy",
        "data": {"bridgeConfigured": False},
        "warnings": [],
        "durationMs": 0,
    }


def test_failure_uses_same_status_as_error_code() -> None:
    """Fail-closed responses provide a stable machine-readable error code."""
    result = ResultEnvelope.failure(
        request_id=uuid4(),
        trace_id=uuid4(),
        status=Status.READ_ONLY,
        message="Writes are disabled",
    )

    assert result.success is False
    assert result.status is Status.READ_ONLY
    assert result.error_code is Status.READ_ONLY


def test_python_status_enum_matches_language_neutral_schema() -> None:
    schema = json.loads((Path("contracts") / "cad-result.schema.json").read_text("utf-8"))

    assert {status.value for status in Status} == set(schema["$defs"]["status"]["enum"])


def test_python_bridge_fields_match_language_neutral_schema() -> None:
    schema = json.loads((Path("contracts") / "bridge-endpoints.schema.json").read_text("utf-8"))
    data = BridgeHealthData(
        service="CadMax.AutoCAD.Bridge",
        instance_id=uuid4(),
        plugin_state=BridgePluginState.READY,
        autocad_connected=True,
        development_host=False,
        document_access=False,
        dwg_read=False,
        dwg_write=False,
        read_only=True,
        allow_write=False,
        allow_script=False,
    ).model_dump(mode="json", by_alias=True)

    assert set(data) == set(schema["$defs"]["health"]["required"])
    assert "autocadConnected" in data
    assert "autoCADConnected" not in data
