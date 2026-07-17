"""Local HTTP backend for the C# AutoCAD bridge host."""

from __future__ import annotations

from time import perf_counter
from typing import Any
from uuid import UUID

import httpx
from pydantic import ValidationError

from cad_max_mcp.backends.base import BackendProbe
from cad_max_mcp.models.command import CadCommandRequest
from cad_max_mcp.models.envelope import ResultEnvelope, Status


class AutoCadBridgeBackend:
    """Call the localhost bridge with bounded timeouts and sanitized failures."""

    def __init__(
        self,
        base_url: str,
        timeout_seconds: float = 5.0,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._timeout = httpx.Timeout(timeout_seconds)
        self._transport = transport

    async def health(self) -> BackendProbe:
        """Probe the bridge health endpoint without exposing connection details."""
        try:
            async with httpx.AsyncClient(
                timeout=self._timeout,
                transport=self._transport,
            ) as client:
                response = await client.get(f"{self._base_url}/health")
                response.raise_for_status()
            return BackendProbe(
                configured=True,
                available=True,
                status=Status.OK,
                message="AutoCAD bridge is available",
            )
        except httpx.TimeoutException:
            return BackendProbe(
                configured=True,
                available=False,
                status=Status.TIMEOUT,
                message="AutoCAD bridge health check timed out",
            )
        except (httpx.HTTPError, ValueError):
            return BackendProbe(
                configured=True,
                available=False,
                status=Status.BACKEND_UNAVAILABLE,
                message="AutoCAD bridge is unavailable",
            )

    async def drawing_status(self, request_id: UUID, trace_id: UUID) -> ResultEnvelope:
        """Dispatch the read-only drawing.status command to the local bridge."""
        started = perf_counter()
        command = CadCommandRequest(
            request_id=request_id,
            trace_id=trace_id,
            command="drawing.status",
        )
        try:
            async with httpx.AsyncClient(
                timeout=self._timeout,
                transport=self._transport,
            ) as client:
                response = await client.post(
                    f"{self._base_url}/v1/commands",
                    json=command.model_dump(mode="json", by_alias=True),
                )
                response.raise_for_status()
                payload: Any = response.json()
            result = ResultEnvelope.model_validate(payload)
            return result.model_copy(
                update={"duration_ms": max(result.duration_ms, _elapsed_ms(started))}
            )
        except httpx.TimeoutException:
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.TIMEOUT,
                message="AutoCAD bridge request timed out",
                duration_ms=_elapsed_ms(started),
            )
        except (httpx.HTTPError, ValueError, ValidationError):
            return ResultEnvelope.failure(
                request_id=request_id,
                trace_id=trace_id,
                status=Status.BACKEND_UNAVAILABLE,
                message="AutoCAD bridge returned no valid response",
                duration_ms=_elapsed_ms(started),
            )

    def capabilities(self) -> list[str]:
        """This bootstrap can only probe and dispatch drawing status."""
        return ["bridge.health", "drawing.status"]


def _elapsed_ms(started: float) -> int:
    """Convert monotonic elapsed time to a non-negative integer."""
    return max(0, int((perf_counter() - started) * 1000))
