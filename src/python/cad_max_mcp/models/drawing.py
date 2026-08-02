"""Strict cross-language models for Phase 1.4 read-only drawing inspection."""

from __future__ import annotations

import math
from datetime import UTC, datetime, timedelta
from enum import StrEnum
from typing import Annotated, Literal
from uuid import UUID

from pydantic import Field, field_validator, model_validator

from cad_max_mcp.models.bridge import BridgeInstanceModel, BridgeModel

DocumentId = Annotated[str, Field(pattern=r"^doc_[A-Za-z0-9_-]{22}$")]


class DrawingOperation(StrEnum):
    STATUS = "status"
    LIST_DOCUMENTS = "list_documents"
    ACTIVE_DOCUMENT = "active_document"
    UNITS = "units"
    BOUNDS = "bounds"
    LAYOUTS = "layouts"
    SYSTEM_METADATA = "system_metadata"


class DrawingInspectRequest(BridgeModel):
    schema_version: Literal["1.0"] = "1.0"
    request_id: UUID
    trace_id: UUID
    deadline_utc: datetime
    expected_instance_id: UUID
    operation: DrawingOperation
    expected_document_id: DocumentId | None = None

    @field_validator("deadline_utc")
    @classmethod
    def validate_deadline(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None:
            raise ValueError("deadlineUtc must include a timezone")
        remaining = value.astimezone(UTC) - datetime.now(UTC)
        if remaining <= timedelta(0) or remaining > timedelta(seconds=10):
            raise ValueError("deadlineUtc must be within ten seconds")
        return value

    @model_validator(mode="after")
    def validate_selector(self) -> DrawingInspectRequest:
        if (
            self.operation in {DrawingOperation.STATUS, DrawingOperation.LIST_DOCUMENTS}
            and self.expected_document_id is not None
        ):
            raise ValueError("application operations do not accept expectedDocumentId")
        if (
            self.operation
            not in {
                DrawingOperation.STATUS,
                DrawingOperation.LIST_DOCUMENTS,
                DrawingOperation.ACTIVE_DOCUMENT,
            }
            and self.expected_document_id is None
        ):
            raise ValueError("document operations require expectedDocumentId")
        return self


class DrawingInspectionData(BridgeInstanceModel):
    dispatch_id: UUID
    operation: DrawingOperation
    main_thread_verified: Literal[True]
    execution_context: Literal["APPLICATION_CONTEXT", "DOCUMENT_COMMAND_CONTEXT"]
    active_document_id: DocumentId | None
    queue_delay_ms: int = Field(ge=0)
    execution_ms: int = Field(ge=0)
    read_mode: Literal["READ_ONLY"]
    transaction_used: bool


class DrawingStatusData(DrawingInspectionData):
    operation: Literal[DrawingOperation.STATUS]
    execution_context: Literal["APPLICATION_CONTEXT"]
    transaction_used: Literal[False]
    runtime_state: Literal["READY"]
    document_state: Literal["ACTIVE", "NO_ACTIVE_DOCUMENT"]
    document_count: int = Field(ge=0, le=64)


class DrawingDocumentData(BridgeModel):
    document_id: DocumentId
    display_name: str = Field(min_length=1, max_length=128)
    is_untitled: bool
    is_active: bool
    context_available: bool

    @field_validator("display_name")
    @classmethod
    def reject_path_display_name(cls, value: str) -> str:
        return _safe_name(value, reject_colon=True)


class DocumentListData(DrawingInspectionData):
    operation: Literal[DrawingOperation.LIST_DOCUMENTS]
    execution_context: Literal["APPLICATION_CONTEXT"]
    transaction_used: Literal[False]
    documents: list[DrawingDocumentData] = Field(max_length=64)
    document_count: int = Field(ge=0, le=64)

    @model_validator(mode="after")
    def validate_count_and_order(self) -> DocumentListData:
        if self.document_count != len(self.documents):
            raise ValueError("documentCount does not match documents")
        expected = sorted(
            self.documents,
            key=lambda item: (not item.is_active, item.document_id),
        )
        if self.documents != expected or sum(item.is_active for item in self.documents) > 1:
            raise ValueError("documents are not stably ordered")
        return self


class ActiveDocumentData(DrawingInspectionData):
    operation: Literal[DrawingOperation.ACTIVE_DOCUMENT]
    execution_context: Literal["DOCUMENT_COMMAND_CONTEXT"]
    active_document_id: DocumentId
    transaction_used: Literal[False]
    document_id: DocumentId
    display_name: str = Field(min_length=1, max_length=128)
    is_untitled: bool
    is_quiescent: Literal[True]
    document_state: Literal["ACTIVE"]

    @field_validator("display_name")
    @classmethod
    def reject_path_display_name(cls, value: str) -> str:
        return _safe_name(value, reject_colon=True)

    @model_validator(mode="after")
    def validate_active_id(self) -> ActiveDocumentData:
        if self.document_id != self.active_document_id:
            raise ValueError("active document identity mismatch")
        return self


class DrawingInsertionUnits(StrEnum):
    UNKNOWN = "UNKNOWN"
    UNITLESS = "UNITLESS"
    INCHES = "INCHES"
    FEET = "FEET"
    MILES = "MILES"
    MILLIMETERS = "MILLIMETERS"
    CENTIMETERS = "CENTIMETERS"
    METERS = "METERS"
    KILOMETERS = "KILOMETERS"
    MICROINCHES = "MICROINCHES"
    MILS = "MILS"
    YARDS = "YARDS"
    ANGSTROMS = "ANGSTROMS"
    NANOMETERS = "NANOMETERS"
    MICRONS = "MICRONS"
    DECIMETERS = "DECIMETERS"
    DEKAMETERS = "DEKAMETERS"
    HECTOMETERS = "HECTOMETERS"
    GIGAMETERS = "GIGAMETERS"
    ASTRONOMICAL_UNITS = "ASTRONOMICAL_UNITS"
    LIGHT_YEARS = "LIGHT_YEARS"
    PARSECS = "PARSECS"
    US_SURVEY_FEET = "US_SURVEY_FEET"
    US_SURVEY_INCHES = "US_SURVEY_INCHES"
    US_SURVEY_YARDS = "US_SURVEY_YARDS"
    US_SURVEY_MILES = "US_SURVEY_MILES"


class DrawingLinearFormat(StrEnum):
    UNKNOWN = "UNKNOWN"
    SCIENTIFIC = "SCIENTIFIC"
    DECIMAL = "DECIMAL"
    ENGINEERING = "ENGINEERING"
    ARCHITECTURAL = "ARCHITECTURAL"
    FRACTIONAL = "FRACTIONAL"


class DrawingAngularFormat(StrEnum):
    UNKNOWN = "UNKNOWN"
    DECIMAL_DEGREES = "DECIMAL_DEGREES"
    DEGREES_MINUTES_SECONDS = "DEGREES_MINUTES_SECONDS"
    GRADIANS = "GRADIANS"
    RADIANS = "RADIANS"
    SURVEYOR_UNITS = "SURVEYOR_UNITS"


class DrawingUnitsData(DrawingInspectionData):
    operation: Literal[DrawingOperation.UNITS]
    execution_context: Literal["DOCUMENT_COMMAND_CONTEXT"]
    active_document_id: DocumentId
    transaction_used: Literal[False]
    insertion_units: DrawingInsertionUnits
    linear_format: DrawingLinearFormat
    linear_precision: int = Field(ge=0, le=8)
    angular_format: DrawingAngularFormat
    angular_precision: int = Field(ge=0, le=8)
    unitless: bool
    millimeters_per_drawing_unit: float | None = Field(default=None, gt=0, le=1e21)

    @field_validator("millimeters_per_drawing_unit")
    @classmethod
    def validate_conversion_factor(cls, value: float | None) -> float | None:
        if value is not None and not math.isfinite(value):
            raise ValueError("millimetersPerDrawingUnit must be finite")
        return value

    @model_validator(mode="after")
    def validate_unitless(self) -> DrawingUnitsData:
        if self.unitless != (self.insertion_units is DrawingInsertionUnits.UNITLESS):
            raise ValueError("unitless flag does not match insertionUnits")
        if (
            self.insertion_units
            in {
                DrawingInsertionUnits.UNITLESS,
                DrawingInsertionUnits.UNKNOWN,
            }
            and self.millimeters_per_drawing_unit is not None
        ):
            raise ValueError("unitless or unknown units cannot have a conversion")
        return self


class DrawingPointData(BridgeModel):
    x: float
    y: float
    z: float

    @model_validator(mode="after")
    def validate_finite_bounded(self) -> DrawingPointData:
        if any(not math.isfinite(value) or abs(value) > 1e15 for value in (self.x, self.y, self.z)):
            raise ValueError("drawing coordinate is invalid")
        return self


class DrawingBoundsData(DrawingInspectionData):
    operation: Literal[DrawingOperation.BOUNDS]
    execution_context: Literal["DOCUMENT_COMMAND_CONTEXT"]
    active_document_id: DocumentId
    transaction_used: Literal[False]
    bounds_state: Literal["AVAILABLE", "EMPTY"]
    source: Literal["DATABASE_EXTENTS"]
    coordinate_system: Literal["WCS"]
    minimum: DrawingPointData | None
    maximum: DrawingPointData | None
    size: DrawingPointData | None
    extents_may_be_stale: Literal[True]

    @model_validator(mode="after")
    def validate_state(self) -> DrawingBoundsData:
        values = (self.minimum, self.maximum, self.size)
        if self.bounds_state == "EMPTY" and any(value is not None for value in values):
            raise ValueError("empty bounds must not contain coordinates")
        if self.bounds_state == "AVAILABLE" and any(value is None for value in values):
            raise ValueError("available bounds require coordinates")
        return self


class DrawingLayoutData(BridgeModel):
    name: str = Field(min_length=1, max_length=128)
    is_model: bool
    tab_order: int = Field(ge=0)
    is_current: bool

    @field_validator("name")
    @classmethod
    def reject_control_name(cls, value: str) -> str:
        return _safe_name(value, reject_colon=True)


class DrawingLayoutsData(DrawingInspectionData):
    operation: Literal[DrawingOperation.LAYOUTS]
    execution_context: Literal["DOCUMENT_COMMAND_CONTEXT"]
    active_document_id: DocumentId
    transaction_used: Literal[True]
    layouts: list[DrawingLayoutData] = Field(max_length=256)
    layout_count: int = Field(ge=0, le=256)

    @model_validator(mode="after")
    def validate_count_and_order(self) -> DrawingLayoutsData:
        if self.layout_count != len(self.layouts):
            raise ValueError("layoutCount does not match layouts")
        expected = sorted(
            self.layouts,
            key=lambda item: (not item.is_model, item.tab_order, item.name),
        )
        if self.layouts != expected or sum(item.is_current for item in self.layouts) > 1:
            raise ValueError("layouts are not stably ordered")
        return self


class DrawingFileFormatVersion(StrEnum):
    UNKNOWN = "UNKNOWN"
    ACAD_R12 = "ACAD_R12"
    ACAD_R13 = "ACAD_R13"
    ACAD_R14 = "ACAD_R14"
    ACAD2000 = "ACAD2000"
    ACAD2004 = "ACAD2004"
    ACAD2007 = "ACAD2007"
    ACAD2010 = "ACAD2010"
    ACAD2013 = "ACAD2013"
    ACAD2018 = "ACAD2018"


class DrawingCurrentSpace(StrEnum):
    UNKNOWN = "UNKNOWN"
    MODEL_SPACE = "MODEL_SPACE"
    PAPER_SPACE = "PAPER_SPACE"


class DrawingSystemMetadataData(DrawingInspectionData):
    operation: Literal[DrawingOperation.SYSTEM_METADATA]
    execution_context: Literal["DOCUMENT_COMMAND_CONTEXT"]
    active_document_id: DocumentId
    transaction_used: Literal[False]
    file_backed: bool
    file_format_version: DrawingFileFormatVersion
    tile_mode: bool
    current_space: DrawingCurrentSpace
    current_layout_name: str = Field(min_length=1, max_length=128)

    @field_validator("current_layout_name")
    @classmethod
    def reject_control_name(cls, value: str) -> str:
        return _safe_name(value, reject_colon=True)


DRAWING_DATA_MODELS = {
    DrawingOperation.STATUS: DrawingStatusData,
    DrawingOperation.LIST_DOCUMENTS: DocumentListData,
    DrawingOperation.ACTIVE_DOCUMENT: ActiveDocumentData,
    DrawingOperation.UNITS: DrawingUnitsData,
    DrawingOperation.BOUNDS: DrawingBoundsData,
    DrawingOperation.LAYOUTS: DrawingLayoutsData,
    DrawingOperation.SYSTEM_METADATA: DrawingSystemMetadataData,
}


def _safe_name(value: str, *, reject_colon: bool) -> str:
    if value in {".", ".."} or any(character in value for character in ("/", "\\")):
        raise ValueError("name must not contain a path separator")
    if reject_colon and ":" in value:
        raise ValueError("displayName must not contain a path or URI")
    if any(ord(character) < 32 or ord(character) == 127 for character in value):
        raise ValueError("name must not contain control characters")
    return value
