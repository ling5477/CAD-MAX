"""CLI smoke tests."""

from __future__ import annotations

import json

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
