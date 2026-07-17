"""Configuration with fail-closed network and write defaults."""

from __future__ import annotations

from ipaddress import ip_address
from pathlib import Path
from urllib.parse import urlparse

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

LOOPBACK_NAMES = frozenset({"localhost"})


def is_loopback_host(host: str) -> bool:
    """Return whether a host is an explicit loopback address or localhost."""
    normalized = host.strip().strip("[]").lower()
    if normalized in LOOPBACK_NAMES:
        return True
    try:
        return ip_address(normalized).is_loopback
    except ValueError:
        return False


class CadMaxSettings(BaseSettings):
    """Runtime settings.

    Settings are sourced from CAD_MAX_ environment variables. Write and script
    execution remain disabled by default and no dotenv file is loaded implicitly.
    """

    model_config = SettingsConfigDict(
        env_prefix="CAD_MAX_",
        case_sensitive=False,
        extra="ignore",
    )

    read_only: bool = True
    allow_write: bool = False
    allow_script: bool = False
    http_host: str = "127.0.0.1"
    http_port: int = Field(default=47771, ge=1, le=65535)
    http_path: str = "/mcp"
    bridge_url: str | None = None
    bridge_timeout_seconds: float = Field(default=5.0, gt=0, le=120)
    allowed_roots: list[Path] = Field(default_factory=list)

    @field_validator("http_host")
    @classmethod
    def require_loopback_http_host(cls, value: str) -> str:
        """Reject remote HTTP exposure; this bootstrap supports localhost only."""
        if not is_loopback_host(value):
            raise ValueError("httpHost must be a loopback address")
        return value

    @field_validator("http_path")
    @classmethod
    def validate_http_path(cls, value: str) -> str:
        """Require one absolute endpoint path without path traversal."""
        if not value.startswith("/") or ".." in value.split("/") or value == "/":
            raise ValueError("httpPath must be an absolute non-root path without traversal")
        return value.rstrip("/")

    @field_validator("bridge_url")
    @classmethod
    def require_local_bridge(cls, value: str | None) -> str | None:
        """Allow only an explicit localhost HTTP bridge URL."""
        if value is None or not value.strip():
            return None
        parsed = urlparse(value)
        if parsed.scheme not in {"http", "https"} or parsed.hostname is None:
            raise ValueError("bridgeUrl must be an HTTP URL")
        if not is_loopback_host(parsed.hostname):
            raise ValueError("bridgeUrl must target loopback")
        return value.rstrip("/")

    @field_validator("allowed_roots")
    @classmethod
    def require_absolute_roots(cls, roots: list[Path]) -> list[Path]:
        """Reject relative roots so authorization never depends on process cwd."""
        if any(not root.is_absolute() for root in roots):
            raise ValueError("allowedRoots entries must be absolute")
        return roots
