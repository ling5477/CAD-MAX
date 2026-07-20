"""Bounded, path-redacted loading of the machine-local bridge token."""

from __future__ import annotations

import base64
import binascii
import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

MAX_TOKEN_FILE_BYTES = 4096
TOKEN_BYTES = 32


class BridgeTokenError(ValueError):
    """A stable token configuration error that never contains the path or value."""

    def __init__(self, error_code: str) -> None:
        super().__init__(error_code)
        self.error_code = error_code


@dataclass(frozen=True, slots=True, repr=False)
class BridgeToken:
    """Bearer token kept out of repr and diagnostics."""

    _encoded: str

    @property
    def authorization_header(self) -> str:
        """Build the header only at the HTTP call boundary."""
        return f"Bearer {self._encoded}"


def load_bridge_token(token_file: Path | None) -> BridgeToken:
    """Load the exact schema and a 256-bit base64url token without leaking its path."""
    if token_file is None:
        raise BridgeTokenError("TOKEN_NOT_CONFIGURED")
    try:
        file_size = token_file.stat().st_size
        if file_size <= 0 or file_size > MAX_TOKEN_FILE_BYTES:
            raise BridgeTokenError("TOKEN_CONFIG_INVALID")
        raw = token_file.read_bytes()
    except FileNotFoundError as error:
        raise BridgeTokenError("TOKEN_NOT_CONFIGURED") from error
    except BridgeTokenError:
        raise
    except OSError as error:
        raise BridgeTokenError("TOKEN_UNAVAILABLE") from error

    try:
        payload: Any = json.loads(raw)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise BridgeTokenError("TOKEN_CONFIG_INVALID") from error

    if not isinstance(payload, dict) or set(payload) != {
        "schemaVersion",
        "token",
        "createdAtUtc",
    }:
        raise BridgeTokenError("TOKEN_CONFIG_INVALID")
    if payload["schemaVersion"] != "1.0" or not isinstance(payload["token"], str):
        raise BridgeTokenError("TOKEN_CONFIG_INVALID")
    if not isinstance(payload["createdAtUtc"], str) or not _is_rfc3339(payload["createdAtUtc"]):
        raise BridgeTokenError("TOKEN_CONFIG_INVALID")

    encoded = payload["token"]
    if len(encoded) != 43:
        raise BridgeTokenError("TOKEN_INVALID")
    try:
        decoded = base64.b64decode(
            encoded.translate(str.maketrans("-_", "+/")) + "=",
            validate=True,
        )
    except (ValueError, binascii.Error) as error:
        raise BridgeTokenError("TOKEN_INVALID") from error
    if len(decoded) != TOKEN_BYTES:
        raise BridgeTokenError("TOKEN_INVALID")
    return BridgeToken(encoded)


def _is_rfc3339(value: str) -> bool:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return parsed.tzinfo is not None
