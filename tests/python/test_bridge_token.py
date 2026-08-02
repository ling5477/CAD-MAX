"""Machine-local bridge token schema tests."""

from __future__ import annotations

import secrets
from pathlib import Path

import pytest

from cad_max_mcp.security.bridge_token import BridgeTokenError, load_bridge_token
from token_file_helpers import grant_insecure_test_reader, write_secure_token_file


def test_valid_token_file(tmp_path: Path) -> None:
    token_file = tmp_path / "bridge-token.json"
    write_secure_token_file(
        token_file,
        {
            "schemaVersion": "1.0",
            "token": secrets.token_urlsafe(32),
            "createdAtUtc": "2026-07-19T00:00:00Z",
        },
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
    write_secure_token_file(token_file, payload)

    with pytest.raises(BridgeTokenError) as raised:
        load_bridge_token(token_file)

    assert raised.value.error_code == error_code


def test_broad_token_reader_fails_closed(tmp_path: Path) -> None:
    token_file = tmp_path / "token.json"
    write_secure_token_file(
        token_file,
        {
            "schemaVersion": "1.0",
            "token": secrets.token_urlsafe(32),
            "createdAtUtc": "2026-07-19T00:00:00Z",
        },
    )
    grant_insecure_test_reader(token_file)

    with pytest.raises(BridgeTokenError) as raised:
        load_bridge_token(token_file)

    assert raised.value.error_code == "TOKEN_FILE_INSECURE"


def test_missing_token_path_is_redacted(tmp_path: Path) -> None:
    missing = tmp_path / "private" / "bridge-token.json"

    with pytest.raises(BridgeTokenError) as raised:
        load_bridge_token(missing)

    assert raised.value.error_code == "TOKEN_NOT_CONFIGURED"
    assert "private" not in str(raised.value)
