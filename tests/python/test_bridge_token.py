"""Machine-local bridge token schema tests."""

from __future__ import annotations

import json
import secrets
from pathlib import Path

import pytest

from cad_max_mcp.security.bridge_token import BridgeTokenError, load_bridge_token


def test_valid_token_file(tmp_path: Path) -> None:
    token_file = tmp_path / "bridge-token.json"
    token_file.write_text(
        json.dumps(
            {
                "schemaVersion": "1.0",
                "token": secrets.token_urlsafe(32),
                "createdAtUtc": "2026-07-19T00:00:00Z",
            }
        ),
        encoding="utf-8",
    )
    token = load_bridge_token(token_file)

    assert token.authorization_header.startswith("Bearer ")


@pytest.mark.parametrize(
    ("payload", "error_code"),
    [
        ({"schemaVersion": "2.0"}, "TOKEN_CONFIG_INVALID"),
        (
            {
                "schemaVersion": "1.0",
                "token": "short",
                "createdAtUtc": "2026-07-19T00:00:00Z",
            },
            "TOKEN_INVALID",
        ),
    ],
)
def test_invalid_schema_and_short_token(
    tmp_path: Path,
    payload: dict[str, object],
    error_code: str,
) -> None:
    token_file = tmp_path / "token.json"
    token_file.write_text(json.dumps(payload), encoding="utf-8")

    with pytest.raises(BridgeTokenError) as raised:
        load_bridge_token(token_file)

    assert raised.value.error_code == error_code


def test_missing_token_path_is_redacted(tmp_path: Path) -> None:
    missing = tmp_path / "private" / "bridge-token.json"

    with pytest.raises(BridgeTokenError) as raised:
        load_bridge_token(missing)

    assert raised.value.error_code == "TOKEN_NOT_CONFIGURED"
    assert "private" not in str(raised.value)
