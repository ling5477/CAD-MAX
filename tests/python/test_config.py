"""Configuration security-default tests."""

from __future__ import annotations

from pathlib import Path

import pytest
from pydantic import ValidationError

from cad_max_mcp.config import CadMaxSettings


def test_safe_defaults(monkeypatch: pytest.MonkeyPatch) -> None:
    """The default process must be local-only and read-only."""
    for name in (
        "CAD_MAX_READ_ONLY",
        "CAD_MAX_ALLOW_WRITE",
        "CAD_MAX_ALLOW_SCRIPT",
        "CAD_MAX_HTTP_HOST",
        "CAD_MAX_ALLOWED_ROOTS",
    ):
        monkeypatch.delenv(name, raising=False)

    settings = CadMaxSettings()

    assert settings.http_host == "127.0.0.1"
    assert settings.http_port == 47771
    assert settings.http_path == "/mcp"
    assert settings.read_only is True
    assert settings.allow_write is False
    assert settings.allow_script is False
    assert settings.allowed_roots == []


def test_non_loopback_http_host_is_rejected() -> None:
    """Remote exposure requires a future, explicit security design."""
    with pytest.raises(ValidationError, match="loopback"):
        CadMaxSettings(http_host="0.0.0.0")


def test_bridge_url_must_be_loopback() -> None:
    """A configured Python-to-C# bridge cannot point to a remote host."""
    with pytest.raises(ValidationError, match="loopback"):
        CadMaxSettings(bridge_url="http://192.0.2.5:47770")


def test_allowed_roots_must_be_absolute() -> None:
    """Authorization roots cannot depend on the process working directory."""
    with pytest.raises(ValidationError, match="absolute"):
        CadMaxSettings(allowed_roots=[Path("relative")])
