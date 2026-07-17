"""Bridge command request model."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field

from cad_max_mcp import SCHEMA_VERSION
from cad_max_mcp.models.envelope import to_camel


class CadCommandRequest(BaseModel):
    """A versioned command sent only to the localhost C# bridge."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)

    schema_version: str = SCHEMA_VERSION
    request_id: UUID
    trace_id: UUID
    command: str = Field(min_length=1, max_length=128)
    parameters: dict[str, Any] = Field(default_factory=dict)
    timeout_ms: int = Field(default=30_000, ge=1, le=120_000)
