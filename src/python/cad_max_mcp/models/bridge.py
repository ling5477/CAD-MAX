"""Typed process-level AutoCAD bridge response data."""

from __future__ import annotations

from datetime import datetime
from enum import StrEnum
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field

from cad_max_mcp.models.envelope import to_camel


class BridgePluginState(StrEnum):
    """Lifecycle states exposed by the SDK-free plugin core."""

    STOPPED = "STOPPED"
    STARTING = "STARTING"
    LISTENING = "LISTENING"
    READY = "READY"
    DEGRADED = "DEGRADED"
    FAILED = "FAILED"
    STOPPING = "STOPPING"


class BridgeConnectionState(StrEnum):
    """Sanitized Python view of the configured bridge connection."""

    NOT_CONFIGURED = "NOT_CONFIGURED"
    NOT_CONNECTED = "NOT_CONNECTED"
    UNAUTHORIZED = "UNAUTHORIZED"
    CONNECTED = "CONNECTED"
    INCOMPATIBLE = "INCOMPATIBLE"
    DEVELOPMENT_HOST = "DEVELOPMENT_HOST"


class BridgeModel(BaseModel):
    """Strict camelCase base for bridge data."""

    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        extra="forbid",
    )


class BridgeInstanceModel(BridgeModel):
    """Base for every endpoint payload carrying process identity."""

    instance_id: UUID


class BridgeHealthData(BridgeInstanceModel):
    service: str = Field(min_length=1, max_length=64)
    plugin_state: BridgePluginState
    autocad_connected: bool
    development_host: bool
    document_access: Literal[False]
    dwg_read: Literal[False]
    dwg_write: Literal[False]
    read_only: Literal[True]
    allow_write: Literal[False]
    allow_script: Literal[False]


class BridgeVersionData(BridgeInstanceModel):
    protocol_version: Literal["1.0"]
    schema_version: Literal["1.0"]
    plugin_version: str = Field(min_length=1, max_length=64)
    adapter_version: str = Field(min_length=1, max_length=64)
    autocad_year: int = Field(ge=0, le=9999)
    autocad_product_version: str = Field(min_length=1, max_length=64)
    runtime_target: str = Field(min_length=1, max_length=64)
    capability_revision: str = Field(min_length=1, max_length=128)
    autocad_connected: bool
    development_host: bool


class BridgeCapabilitiesData(BridgeInstanceModel):
    capability_revision: str = Field(min_length=1, max_length=128)
    plugin_state: BridgePluginState
    autocad_connected: bool
    development_host: bool
    capabilities: dict[str, bool]


class BridgeHeartbeatData(BridgeInstanceModel):
    heartbeat_sequence: int = Field(ge=1)
    timestamp_utc: datetime
    uptime_ms: int = Field(ge=0)
    plugin_state: BridgePluginState
    capability_revision: str = Field(min_length=1, max_length=128)
    autocad_connected: bool
    development_host: bool
