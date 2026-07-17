"""Null backend for machines without an AutoCAD bridge."""

from __future__ import annotations

from uuid import UUID

from cad_max_mcp.backends.base import BackendProbe
from cad_max_mcp.models.envelope import ResultEnvelope, Status


class NullCadBackend:
    """Fail-closed backend used when no bridge URL is configured."""

    async def health(self) -> BackendProbe:
        """Report explicit non-configuration without attempting any connection."""
        return BackendProbe(
            configured=False,
            available=False,
            status=Status.BACKEND_NOT_CONFIGURED,
            message="AutoCAD bridge is not configured",
        )

    async def drawing_status(self, request_id: UUID, trace_id: UUID) -> ResultEnvelope:
        """Never fabricate a drawing when the bridge is absent."""
        return ResultEnvelope.failure(
            request_id=request_id,
            trace_id=trace_id,
            status=Status.BACKEND_NOT_CONFIGURED,
            message="AutoCAD bridge is not configured",
        )

    def capabilities(self) -> list[str]:
        """The null backend provides no CAD capabilities."""
        return []
