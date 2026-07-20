"""Null backend for machines without an AutoCAD bridge."""

from __future__ import annotations

from uuid import UUID, uuid4

from cad_max_mcp.backends.base import BackendProbe
from cad_max_mcp.models.bridge import BridgeConnectionState
from cad_max_mcp.models.envelope import ResultEnvelope, Status


class NullCadBackend:
    """Fail-closed backend used when no bridge URL is configured."""

    @property
    def connection_state(self) -> BridgeConnectionState:
        """An absent backend is explicitly not configured."""
        return BridgeConnectionState.NOT_CONFIGURED

    async def health(self) -> BackendProbe:
        """Report explicit non-configuration without attempting any connection."""
        return BackendProbe(
            configured=False,
            available=False,
            status=Status.BACKEND_NOT_CONFIGURED,
            message="AutoCAD bridge is not configured",
            connection_state=BridgeConnectionState.NOT_CONFIGURED,
        )

    async def bridge_system(self, operation: str) -> ResultEnvelope:
        """No process-level endpoint exists without explicit configuration."""
        del operation
        return ResultEnvelope.failure(
            request_id=uuid4(),
            trace_id=uuid4(),
            status=Status.BACKEND_NOT_CONFIGURED,
            message="AutoCAD bridge is not configured",
            data={"connectionState": BridgeConnectionState.NOT_CONFIGURED.value},
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
