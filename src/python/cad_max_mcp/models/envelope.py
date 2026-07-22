"""Versioned response envelope shared with the C# bridge."""

from __future__ import annotations

from enum import StrEnum
from typing import Any, Literal
from uuid import UUID, uuid4

from pydantic import BaseModel, ConfigDict, Field


def to_camel(value: str) -> str:
    """Convert snake_case model field names to camelCase JSON names."""
    first, *rest = value.split("_")
    return first + "".join(part.capitalize() for part in rest)


class Status(StrEnum):
    """Stable protocol status and error code values."""

    OK = "OK"
    BACKEND_UNAVAILABLE = "BACKEND_UNAVAILABLE"
    BACKEND_NOT_CONFIGURED = "BACKEND_NOT_CONFIGURED"
    INVALID_ARGUMENT = "INVALID_ARGUMENT"
    PATH_NOT_ALLOWED = "PATH_NOT_ALLOWED"
    READ_ONLY = "READ_ONLY"
    NOT_IMPLEMENTED = "NOT_IMPLEMENTED"
    TIMEOUT = "TIMEOUT"
    CANCELLED = "CANCELLED"
    DISPATCHER_NOT_READY = "DISPATCHER_NOT_READY"
    INSTANCE_MISMATCH = "INSTANCE_MISMATCH"
    NO_ACTIVE_DOCUMENT = "NO_ACTIVE_DOCUMENT"
    DOCUMENT_NOT_ACTIVE = "DOCUMENT_NOT_ACTIVE"
    DOCUMENT_DESTROYED = "DOCUMENT_DESTROYED"
    DOCUMENT_NOT_FOUND = "DOCUMENT_NOT_FOUND"
    APPLICATION_MODAL = "APPLICATION_MODAL"
    DOCUMENT_BUSY = "DOCUMENT_BUSY"
    QUEUE_FULL = "QUEUE_FULL"
    MAIN_THREAD_DISPATCH_FAILED = "MAIN_THREAD_DISPATCH_FAILED"
    COMMAND_CONTEXT_FAILED = "COMMAND_CONTEXT_FAILED"
    INTERNAL_ERROR = "INTERNAL_ERROR"
    UNAUTHORIZED = "UNAUTHORIZED"
    METHOD_NOT_ALLOWED = "METHOD_NOT_ALLOWED"
    ROUTE_NOT_FOUND = "ROUTE_NOT_FOUND"
    QUERY_NOT_ALLOWED = "QUERY_NOT_ALLOWED"
    REQUEST_BODY_NOT_ALLOWED = "REQUEST_BODY_NOT_ALLOWED"
    HEADERS_TOO_LARGE = "HEADERS_TOO_LARGE"
    REQUEST_TARGET_TOO_LONG = "REQUEST_TARGET_TOO_LONG"
    REQUEST_TIMEOUT = "REQUEST_TIMEOUT"
    SERVER_BUSY = "SERVER_BUSY"
    BRIDGE_NOT_READY = "BRIDGE_NOT_READY"
    BRIDGE_STOPPING = "BRIDGE_STOPPING"
    NOT_CONNECTED = "NOT_CONNECTED"
    SCHEMA_MISMATCH = "SCHEMA_MISMATCH"
    TOKEN_NOT_CONFIGURED = "TOKEN_NOT_CONFIGURED"
    TOKEN_CONFIG_INVALID = "TOKEN_CONFIG_INVALID"
    TOKEN_FILE_INSECURE = "TOKEN_FILE_INSECURE"
    TOKEN_INVALID = "TOKEN_INVALID"
    TOKEN_UNAVAILABLE = "TOKEN_UNAVAILABLE"


class ResultEnvelope(BaseModel):
    """A client-safe response with correlation, status, data, and timing."""

    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        use_enum_values=False,
        extra="forbid",
    )

    schema_version: Literal["1.0"] = "1.0"
    request_id: UUID = Field(default_factory=uuid4)
    trace_id: UUID = Field(default_factory=uuid4)
    success: bool
    status: Status
    error_code: Status | None = None
    message: str
    data: dict[str, Any] = Field(default_factory=dict)
    warnings: list[str] = Field(default_factory=list)
    duration_ms: int = Field(default=0, ge=0)

    def to_wire(self) -> dict[str, Any]:
        """Serialize with camelCase names and JSON-compatible UUID/enum values."""
        return self.model_dump(mode="json", by_alias=True)

    @classmethod
    def ok(
        cls,
        *,
        request_id: UUID,
        trace_id: UUID,
        message: str,
        data: dict[str, Any] | None = None,
        duration_ms: int = 0,
    ) -> ResultEnvelope:
        """Build a successful response."""
        return cls(
            request_id=request_id,
            trace_id=trace_id,
            success=True,
            status=Status.OK,
            message=message,
            data=data or {},
            duration_ms=duration_ms,
        )

    @classmethod
    def failure(
        cls,
        *,
        request_id: UUID,
        trace_id: UUID,
        status: Status,
        message: str,
        data: dict[str, Any] | None = None,
        duration_ms: int = 0,
    ) -> ResultEnvelope:
        """Build a fail-closed response without exception details."""
        return cls(
            request_id=request_id,
            trace_id=trace_id,
            success=False,
            status=status,
            error_code=status,
            message=message,
            data=data or {},
            duration_ms=duration_ms,
        )
