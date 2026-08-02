"""Cross-language drawing DTO validation and fail-closed boundary tests."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from pydantic import ValidationError

from cad_max_mcp.models.drawing import (
    DocumentListData,
    DrawingBoundsData,
    DrawingInspectRequest,
    DrawingLayoutData,
    DrawingOperation,
    DrawingSystemMetadataData,
    DrawingUnitsData,
)


def common_data(operation: str) -> dict[str, object]:
    return {
        "instanceId": str(uuid4()),
        "dispatchId": str(uuid4()),
        "operation": operation,
        "mainThreadVerified": True,
        "executionContext": "DOCUMENT_COMMAND_CONTEXT",
        "activeDocumentId": "doc_AAAAAAAAAAAAAAAAAAAAAA",
        "queueDelayMs": 0,
        "executionMs": 0,
        "readMode": "READ_ONLY",
        "transactionUsed": False,
    }


def test_request_enum_selector_and_extra_fields_are_strict() -> None:
    payload = {
        "schemaVersion": "1.0",
        "requestId": str(uuid4()),
        "traceId": str(uuid4()),
        "deadlineUtc": (datetime.now(UTC) + timedelta(seconds=5)).isoformat(),
        "expectedInstanceId": str(uuid4()),
        "operation": "status",
        "expectedDocumentId": None,
    }
    assert DrawingInspectRequest.model_validate(payload).operation is DrawingOperation.STATUS

    with pytest.raises(ValidationError):
        DrawingInspectRequest.model_validate({**payload, "operation": "query_entities"})
    with pytest.raises(ValidationError):
        DrawingInspectRequest.model_validate(
            {**payload, "expectedDocumentId": "doc_AAAAAAAAAAAAAAAAAAAAAA"}
        )
    with pytest.raises(ValidationError):
        DrawingInspectRequest.model_validate({**payload, "arguments": {}})


def test_document_list_rejects_path_and_unstable_order() -> None:
    payload = {
        **common_data("list_documents"),
        "executionContext": "APPLICATION_CONTEXT",
        "transactionUsed": False,
        "documents": [
            {
                "documentId": "doc_BBBBBBBBBBBBBBBBBBBBBB",
                "displayName": "b.dwg",
                "isUntitled": False,
                "isActive": False,
                "contextAvailable": False,
            },
            {
                "documentId": "doc_AAAAAAAAAAAAAAAAAAAAAA",
                "displayName": "a.dwg",
                "isUntitled": False,
                "isActive": True,
                "contextAvailable": True,
            },
        ],
        "documentCount": 2,
    }
    with pytest.raises(ValidationError):
        DocumentListData.model_validate(payload)

    payload["documents"] = list(reversed(payload["documents"]))  # type: ignore[arg-type]
    payload["documents"][0]["displayName"] = (  # type: ignore[index]
        r"C:\CADMAX_SECRET_DIRECTORY_SENTINEL\fixture.dwg"
    )
    with pytest.raises(ValidationError):
        DocumentListData.model_validate(payload)


@pytest.mark.parametrize("invalid", [float("nan"), float("inf"), 1.1e15])
def test_bounds_reject_nonfinite_and_extreme_values(invalid: float) -> None:
    payload = {
        **common_data("bounds"),
        "boundsState": "AVAILABLE",
        "source": "DATABASE_EXTENTS",
        "coordinateSystem": "WCS",
        "minimum": {"x": 0, "y": 0, "z": 0},
        "maximum": {"x": invalid, "y": 1, "z": 1},
        "size": {"x": 1, "y": 1, "z": 1},
        "extentsMayBeStale": True,
    }
    with pytest.raises(ValidationError):
        DrawingBoundsData.model_validate(payload)


def test_units_do_not_attach_conversion_to_unitless_or_unknown() -> None:
    payload = {
        **common_data("units"),
        "insertionUnits": "UNITLESS",
        "linearFormat": "DECIMAL",
        "linearPrecision": 4,
        "angularFormat": "DECIMAL_DEGREES",
        "angularPrecision": 2,
        "unitless": True,
        "millimetersPerDrawingUnit": 1.0,
    }
    with pytest.raises(ValidationError):
        DrawingUnitsData.model_validate(payload)


@pytest.mark.parametrize("invalid", [float("inf"), 1e22])
def test_units_reject_nonfinite_or_excessive_conversion_factor(invalid: float) -> None:
    payload = {
        **common_data("units"),
        "insertionUnits": "PARSECS",
        "linearFormat": "DECIMAL",
        "linearPrecision": 4,
        "angularFormat": "DECIMAL_DEGREES",
        "angularPrecision": 2,
        "unitless": False,
        "millimetersPerDrawingUnit": invalid,
    }
    with pytest.raises(ValidationError):
        DrawingUnitsData.model_validate(payload)


def test_units_accept_parsec_conversion_within_safe_bound() -> None:
    data = DrawingUnitsData.model_validate(
        {
            **common_data("units"),
            "insertionUnits": "PARSECS",
            "linearFormat": "DECIMAL",
            "linearPrecision": 4,
            "angularFormat": "DECIMAL_DEGREES",
            "angularPrecision": 2,
            "unitless": False,
            "millimetersPerDrawingUnit": 3.085677581491367e19,
        }
    )
    assert data.millimeters_per_drawing_unit is not None


@pytest.mark.parametrize("unsafe_name", ["Sheet/1", r"Sheet\\1", "C:Sheet", ".", ".."])
def test_layout_and_current_layout_names_reject_path_like_values(unsafe_name: str) -> None:
    with pytest.raises(ValidationError):
        DrawingLayoutData.model_validate(
            {
                "name": unsafe_name,
                "isModel": False,
                "tabOrder": 1,
                "isCurrent": False,
            }
        )
    with pytest.raises(ValidationError):
        DrawingSystemMetadataData.model_validate(
            {
                **common_data("system_metadata"),
                "fileBacked": True,
                "fileFormatVersion": "ACAD2018",
                "tileMode": True,
                "currentSpace": "MODEL_SPACE",
                "currentLayoutName": unsafe_name,
            }
        )
