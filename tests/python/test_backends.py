"""Backend behavior tests without a real AutoCAD installation."""

from __future__ import annotations

from uuid import uuid4

import httpx

from cad_max_mcp.backends import AutoCadBridgeBackend, NullCadBackend
from cad_max_mcp.models import Status


async def test_null_backend_reports_not_configured() -> None:
    """No bridge must never look like a connected drawing."""
    backend = NullCadBackend()

    probe = await backend.health()
    result = await backend.drawing_status(uuid4(), uuid4())

    assert probe.status is Status.BACKEND_NOT_CONFIGURED
    assert probe.configured is False
    assert result.status is Status.BACKEND_NOT_CONFIGURED
    assert result.success is False


async def test_bridge_connection_failure_is_sanitized() -> None:
    """Connection details and local paths are not returned to MCP clients."""

    async def fail_connect(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("sensitive local detail", request=request)

    backend = AutoCadBridgeBackend(
        "http://127.0.0.1:47770",
        transport=httpx.MockTransport(fail_connect),
    )

    probe = await backend.health()
    result = await backend.drawing_status(uuid4(), uuid4())

    assert probe.status is Status.BACKEND_UNAVAILABLE
    assert "sensitive" not in probe.message
    assert result.status is Status.BACKEND_UNAVAILABLE
    assert "sensitive" not in result.message
