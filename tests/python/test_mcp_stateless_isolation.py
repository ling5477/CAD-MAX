"""Cross-client isolation tests for explicit AutoCAD state handles."""

from __future__ import annotations

import asyncio
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx

from cad_max_mcp.models import ResultEnvelope, Status
from cad_max_mcp.models.drawing import DrawingOperation
from cad_max_mcp.server import create_server
from test_mcp_v2_protocol import ProtocolBackend, _post_modern, _settings

DOCUMENT_A = "doc_AAAAAAAAAAAAAAAAAAAAAA"
DOCUMENT_B = "doc_BBBBBBBBBBBBBBBBBBBBBB"


class IsolatedBackend(ProtocolBackend):
    """Model two explicit instances without retaining a per-client selector."""

    def __init__(self) -> None:
        super().__init__()
        self.instance_a = uuid4()
        self.instance_b = uuid4()
        self.documents = {
            self.instance_a: DOCUMENT_A,
            self.instance_b: DOCUMENT_B,
        }

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
        del deadline_ms
        self.calls.append((request_id, trace_id))
        if operation is not DrawingOperation.ACTIVE_DOCUMENT or expected_instance_id is None:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.INVALID_ARGUMENT,
                message="Explicit instance handle is required",
            )
        document_id = self.documents.get(expected_instance_id)
        if document_id is None:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.INSTANCE_MISMATCH,
                message="Instance handle does not match",
            )
        if expected_document_id is not None and expected_document_id != document_id:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.DOCUMENT_NOT_ACTIVE,
                message="Document handle does not match",
            )
        return ResultEnvelope.ok(
            request_id=request_id,
            trace_id=trace_id,
            message="Explicit handles accepted",
            data={
                "instanceId": str(expected_instance_id),
                "dispatchId": str(uuid4()),
                "operation": "active_document",
                "mainThreadVerified": True,
                "executionContext": "DOCUMENT_COMMAND_CONTEXT",
                "activeDocumentId": document_id,
                "queueDelayMs": 0,
                "executionMs": 0,
                "readMode": "READ_ONLY",
                "transactionUsed": False,
                "documentId": document_id,
                "displayName": "fixture.dwg",
                "isUntitled": True,
                "isQuiescent": True,
                "documentState": "ACTIVE",
            },
        )


def _active_arguments(instance_id: UUID | None, document_id: str | None = None) -> dict[str, Any]:
    arguments: dict[str, Any] = {"operation": "active_document"}
    if instance_id is not None:
        arguments["expected_instance_id"] = str(instance_id)
    if document_id is not None:
        arguments["expected_document_id"] = document_id
    return {"name": "drawing", "arguments": arguments}


async def test_two_clients_never_share_instance_document_or_trace_state(tmp_path: Path) -> None:
    settings, token = _settings(tmp_path)
    backend = IsolatedBackend()
    app = create_server(settings, backend, "streamable-http").streamable_http_app()
    request_a = uuid4()
    request_b = uuid4()
    trace_a = uuid4()
    trace_b = uuid4()

    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://127.0.0.1:47771",
            headers={"Authorization": f"Bearer {token}"},
        ) as client_b:
            async with httpx.AsyncClient(
                transport=httpx.ASGITransport(app=app),
                base_url="http://127.0.0.1:47771",
                headers={"Authorization": f"Bearer {token}"},
            ) as client_a:
                response_a, response_b = await asyncio.gather(
                    _post_modern(
                        client_a,
                        "tools/call",
                        request_id=str(request_a),
                        params=_active_arguments(backend.instance_a, DOCUMENT_A),
                        meta={"traceparent": f"00-{trace_a.hex}-0123456789abcdef-01"},
                    ),
                    _post_modern(
                        client_b,
                        "tools/call",
                        request_id=str(request_b),
                        params=_active_arguments(backend.instance_b, DOCUMENT_B),
                        meta={"traceparent": f"00-{trace_b.hex}-fedcba9876543210-01"},
                    ),
                )
                omitted = await _post_modern(
                    client_b,
                    "tools/call",
                    params=_active_arguments(None),
                )
                wrong_instance = await _post_modern(
                    client_b,
                    "tools/call",
                    params=_active_arguments(uuid4()),
                )

            after_disconnect = await _post_modern(
                client_b,
                "tools/call",
                params=_active_arguments(backend.instance_b, DOCUMENT_B),
            )

    content_a = response_a.json()["result"]["structuredContent"]
    content_b = response_b.json()["result"]["structuredContent"]
    assert content_a["requestId"] == str(request_a)
    assert content_a["traceId"] == str(trace_a)
    assert content_a["data"]["documentId"] == DOCUMENT_A
    assert content_b["requestId"] == str(request_b)
    assert content_b["traceId"] == str(trace_b)
    assert content_b["data"]["documentId"] == DOCUMENT_B
    assert omitted.json()["result"]["structuredContent"]["status"] == "INVALID_ARGUMENT"
    assert wrong_instance.json()["result"]["structuredContent"]["status"] == "INSTANCE_MISMATCH"
    assert (
        after_disconnect.json()["result"]["structuredContent"]["data"]["documentId"] == DOCUMENT_B
    )
    for response in (response_a, response_b, omitted, wrong_instance, after_disconnect):
        assert "mcp-session-id" not in response.headers
