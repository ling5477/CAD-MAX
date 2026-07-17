"""Wire-contract serialization tests."""

from __future__ import annotations

from uuid import uuid4

from cad_max_mcp.models import ResultEnvelope, Status


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
