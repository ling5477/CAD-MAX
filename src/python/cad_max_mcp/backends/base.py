"""Backend protocol used by MCP tools."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol
from uuid import UUID

from cad_max_mcp.models.envelope import ResultEnvelope, Status


@dataclass(frozen=True, slots=True)
class BackendProbe:
    """Sanitized bridge health state."""

    configured: bool
    available: bool
    status: Status
    message: str


class CadBackend(Protocol):
    """Backend contract.

    Implementations must map dependency failures to structured results and must not
    expose network details, local paths, environment values, or raw stack traces.
    """

    async def health(self) -> BackendProbe:
        """Probe backend availability without mutating CAD state."""
        ...

    async def drawing_status(self, request_id: UUID, trace_id: UUID) -> ResultEnvelope:
        """Return real drawing status or an explicit fail-closed result."""
        ...

    def capabilities(self) -> list[str]:
        """Return only capabilities implemented by this backend."""
        ...
