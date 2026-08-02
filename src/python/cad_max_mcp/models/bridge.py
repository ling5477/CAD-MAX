"""Typed process-level AutoCAD bridge response data."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from enum import StrEnum
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, field_validator

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
    document_access: bool
    dwg_read: bool
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
    context_dispatcher_state: Literal["NOT_READY", "READY", "STOPPING"]
    queue_depth: int = Field(ge=0, le=32)
    in_flight_count: int = Field(ge=0, le=1)
    modal: bool
    has_active_document: bool
    last_dispatch_status: Literal[
        "NONE",
        "OK",
        "NO_ACTIVE_DOCUMENT",
        "DOCUMENT_BUSY",
        "APPLICATION_MODAL",
        "TIMEOUT",
        "CANCELLED",
        "FAILED",
    ]


class ContextProbeRequest(BridgeModel):
    """Strict request for the sole Phase 1.3 document-context endpoint."""

    schema_version: Literal["1.0"] = "1.0"
    request_id: UUID
    trace_id: UUID
    deadline_utc: datetime
    expected_instance_id: UUID
    expected_document_id: str | None = Field(
        default=None,
        pattern=r"^doc_[A-Za-z0-9_-]{22}$",
    )

    @field_validator("deadline_utc")
    @classmethod
    def validate_deadline(cls, value: datetime) -> datetime:
        """Require an aware future deadline no more than ten seconds away."""
        if value.tzinfo is None or value.utcoffset() is None:
            raise ValueError("deadlineUtc must include a timezone")
        remaining = value.astimezone(UTC) - datetime.now(UTC)
        if remaining <= timedelta(0) or remaining > timedelta(seconds=10):
            raise ValueError("deadlineUtc must be within ten seconds")
        return value


class ContextProbeData(BridgeInstanceModel):
    """Safe result from the fixed document command-context callback."""

    dispatch_id: UUID
    main_thread_verified: Literal[True]
    execution_context: Literal["DOCUMENT_COMMAND_CONTEXT"]
    document_state: Literal["ACTIVE"]
    active_document_id: str = Field(pattern=r"^doc_[A-Za-z0-9_-]{22}$")
    is_quiescent: Literal[True]
    queue_delay_ms: int = Field(ge=0)
    execution_ms: int = Field(ge=0)
