"""Strict structured-output models for the public MCP tools."""

from __future__ import annotations

from typing import Annotated, Literal
from uuid import UUID

from pydantic import Field, model_validator

from cad_max_mcp.models.bridge import (
    BridgeConnectionState,
    BridgeHealthData,
    BridgeModel,
    BridgePluginState,
    BridgeVersionData,
)
from cad_max_mcp.models.drawing import (
    ActiveDocumentData,
    DocumentListData,
    DrawingBoundsData,
    DrawingLayoutsData,
    DrawingStatusData,
    DrawingSystemMetadataData,
    DrawingUnitsData,
)
from cad_max_mcp.models.envelope import Status

CapabilityName = Annotated[str, Field(pattern=r"^[A-Za-z][A-Za-z0-9_.-]{0,127}$")]


class EmptyToolData(BridgeModel):
    """An explicit empty result object used by structured failures."""


class ConnectionStateToolData(BridgeModel):
    """The only additional data allowed on a backend-not-configured failure."""

    connection_state: BridgeConnectionState


class CapabilityEntry(BridgeModel):
    """One bounded capability entry replacing an open-ended public object map."""

    name: CapabilityName
    enabled: bool


class BridgeCapabilitiesToolData(BridgeModel):
    """Closed public projection of the internal bridge capability response."""

    instance_id: UUID
    capability_revision: str = Field(min_length=1, max_length=128)
    plugin_state: BridgePluginState
    autocad_connected: bool
    development_host: bool
    capabilities: list[CapabilityEntry] = Field(max_length=256)


BridgeToolData = (
    EmptyToolData
    | ConnectionStateToolData
    | BridgeHealthData
    | BridgeVersionData
    | BridgeCapabilitiesToolData
)


class ToolResultEnvelope[DataT](BridgeModel):
    """Stable public envelope with a closed, tool-specific data schema."""

    schema_version: Literal["1.0"] = "1.0"
    request_id: UUID
    trace_id: UUID
    success: bool
    status: Status
    error_code: Status | None = None
    message: str = Field(min_length=1, max_length=256)
    data: DataT
    warnings: list[str] = Field(default_factory=list, max_length=32)
    duration_ms: int = Field(default=0, ge=0)

    @model_validator(mode="after")
    def validate_status_consistency(self) -> ToolResultEnvelope[DataT]:
        """Prevent success/error fields from contradicting each other on the wire."""
        if self.success:
            if self.status is not Status.OK or self.error_code is not None:
                raise ValueError("successful result has inconsistent status")
        elif self.status is Status.OK or self.error_code is not self.status:
            raise ValueError("failed result has inconsistent status")
        return self


BridgeToolEnvelope = ToolResultEnvelope[BridgeToolData]


class CadSystemCommonData(BridgeModel):
    """Fields shared by every cad_system operation."""

    server_name: Literal["CAD-MAX"]
    server_version: str = Field(min_length=1, max_length=64)
    schema_version: Literal["1.0"]
    transport: Literal["stdio", "streamable-http"]
    bridge_configured: bool
    bridge_available: bool
    bridge_status: Status
    bridge_connection_state: BridgeConnectionState
    auto_cad_bridge: BridgeToolEnvelope
    read_only: bool
    allow_write: bool
    allow_script: bool
    allowed_roots_count: int = Field(ge=0, le=1024)


class CadSystemHealthData(CadSystemCommonData):
    python_version: str = Field(min_length=1, max_length=64)
    bridge_message: str = Field(min_length=1, max_length=256)


class CadSystemVersionData(CadSystemCommonData):
    python_version: str = Field(min_length=1, max_length=64)


class CadSystemCapabilitiesData(CadSystemCommonData):
    implemented: list[CapabilityName] = Field(max_length=256)
    backend_implemented: list[CapabilityName] = Field(max_length=256)
    not_implemented: list[CapabilityName] = Field(max_length=256)


CadSystemToolData = CadSystemHealthData | CadSystemVersionData | CadSystemCapabilitiesData
CadSystemToolResult = ToolResultEnvelope[CadSystemToolData]

DrawingToolData = (
    EmptyToolData
    | DrawingStatusData
    | DocumentListData
    | ActiveDocumentData
    | DrawingUnitsData
    | DrawingBoundsData
    | DrawingLayoutsData
    | DrawingSystemMetadataData
)
DrawingToolResult = ToolResultEnvelope[DrawingToolData]
