"""Construct the configured fail-closed backend."""

from cad_max_mcp.backends import AutoCadBridgeBackend, CadBackend, NullCadBackend
from cad_max_mcp.config import CadMaxSettings


def create_backend(settings: CadMaxSettings) -> CadBackend:
    """Use a null backend unless an explicit local bridge URL is configured."""
    if settings.bridge_url is None:
        return NullCadBackend()
    return AutoCadBridgeBackend(settings.bridge_url, settings.bridge_timeout_seconds)
