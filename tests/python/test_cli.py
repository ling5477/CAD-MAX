"""CLI smoke tests."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from cad_max_mcp.backends import AutoCadBridgeBackend
from cad_max_mcp.cli import main


def test_doctor_outputs_json_and_returns_zero(capsys: object) -> None:
    """A valid safe configuration produces an OK diagnostic result."""
    assert main(["doctor"]) == 0

    captured = capsys.readouterr()  # type: ignore[attr-defined]
    report = json.loads(captured.out)
    assert report["status"] == "OK"
    assert report["configurationValid"] is True
    assert report["httpHost"] == "127.0.0.1"
    assert report["allowWrite"] is False


def test_bridge_doctor_unconfigured_returns_nonzero_json(
    monkeypatch: pytest.MonkeyPatch,
    capsys: object,
) -> None:
    monkeypatch.delenv("CAD_MAX_BRIDGE_URL", raising=False)
    monkeypatch.delenv("CAD_MAX_BRIDGE_TOKEN_FILE", raising=False)

    assert main(["bridge-doctor"]) == 2

    report = json.loads(capsys.readouterr().out)  # type: ignore[attr-defined]
    assert report["status"] == "BACKEND_NOT_CONFIGURED"
    assert report["connectionState"] == "NOT_CONFIGURED"


def test_bridge_doctor_success_exit_code_is_forwarded(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    capsys: object,
) -> None:
    async def fake_doctor(
        self: AutoCadBridgeBackend,
    ) -> tuple[int, dict[str, object]]:
        del self
        return 0, {"schemaVersion": "1.0", "status": "OK", "connected": True}

    monkeypatch.setenv("CAD_MAX_BRIDGE_URL", "http://127.0.0.1:47770")
    monkeypatch.setenv("CAD_MAX_BRIDGE_TOKEN_FILE", str(tmp_path / "token.json"))
    monkeypatch.setattr(AutoCadBridgeBackend, "bridge_doctor", fake_doctor)

    assert main(["bridge-doctor"]) == 0

    report = json.loads(capsys.readouterr().out)  # type: ignore[attr-defined]
    assert report == {"connected": True, "schemaVersion": "1.0", "status": "OK"}


def test_context_doctor_success_exit_code_is_forwarded(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    capsys: object,
) -> None:
    async def fake_context_doctor(
        self: AutoCadBridgeBackend,
    ) -> tuple[int, dict[str, object]]:
        del self
        return 0, {
            "schemaVersion": "1.0",
            "status": "OK",
            "mainThreadVerified": True,
        }

    monkeypatch.setenv("CAD_MAX_BRIDGE_URL", "http://127.0.0.1:47770")
    monkeypatch.setenv("CAD_MAX_BRIDGE_TOKEN_FILE", str(tmp_path / "token.json"))
    monkeypatch.setattr(AutoCadBridgeBackend, "context_doctor", fake_context_doctor)

    assert main(["context-doctor"]) == 0

    report = json.loads(capsys.readouterr().out)  # type: ignore[attr-defined]
    assert report["mainThreadVerified"] is True


def test_drawing_doctor_success_exit_code_is_forwarded(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    capsys: object,
) -> None:
    expected_names: list[str | None] = []

    async def fake_drawing_doctor(
        self: AutoCadBridgeBackend,
        expected_active_document_name: str | None = None,
    ) -> tuple[int, dict[str, object]]:
        del self
        expected_names.append(expected_active_document_name)
        return 0, {
            "schemaVersion": "1.0",
            "status": "OK",
            "activeDocument": "REDACTED",
            "dwgRead": True,
        }

    monkeypatch.setenv("CAD_MAX_BRIDGE_URL", "http://127.0.0.1:47770")
    monkeypatch.setenv("CAD_MAX_BRIDGE_TOKEN_FILE", str(tmp_path / "token.json"))
    monkeypatch.setattr(AutoCadBridgeBackend, "drawing_doctor", fake_drawing_doctor)

    assert main(["drawing-doctor", "--expected-active-document-name", "fixture.dwg"]) == 0

    captured = capsys.readouterr()  # type: ignore[attr-defined]
    report = json.loads(captured.out)
    assert report["activeDocument"] == "REDACTED"
    assert report["dwgRead"] is True
    assert expected_names == ["fixture.dwg"]
    assert "fixture.dwg" not in captured.out


def test_streamable_http_requires_a_configured_caller_token(
    monkeypatch: pytest.MonkeyPatch,
    capsys: object,
) -> None:
    monkeypatch.delenv("CAD_MAX_MCP_HTTP_TOKEN_FILE", raising=False)

    assert main(["serve", "--transport", "streamable-http"]) == 2

    report = json.loads(capsys.readouterr().out)  # type: ignore[attr-defined]
    assert report == {
        "configurationValid": False,
        "message": "Streamable HTTP caller authentication is unavailable",
        "schemaVersion": "1.0",
        "serverName": "CAD-MAX",
        "status": "MCP_HTTP_AUTH_NOT_CONFIGURED",
    }
