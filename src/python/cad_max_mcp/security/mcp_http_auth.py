"""Static caller authentication for the loopback Streamable HTTP MCP endpoint."""

from __future__ import annotations

import hmac
from pathlib import Path

from mcp.server.auth.provider import AccessToken, TokenVerifier

from cad_max_mcp.security.bridge_token import BridgeTokenError, load_bridge_token

MCP_HTTP_READ_SCOPE = "cad-max:read"


class McpHttpAuthError(ValueError):
    """Stable Streamable HTTP auth configuration failure without token details."""

    def __init__(self, error_code: str) -> None:
        super().__init__(error_code)
        self.error_code = error_code


class LocalMcpHttpTokenVerifier:
    """Verify one machine-local caller token without exposing it in diagnostics."""

    def __init__(self, expected_token: str) -> None:
        self._expected_token = expected_token

    async def verify_token(self, token: str) -> AccessToken | None:
        """Return a fixed read-only principal only for a constant-time token match."""
        if (
            not isinstance(token, str)
            or not token.isascii()
            or not self._expected_token.isascii()
            or not hmac.compare_digest(token, self._expected_token)
        ):
            return None
        return AccessToken(
            token=token,
            client_id="cad-max-local-mcp",
            scopes=[MCP_HTTP_READ_SCOPE],
            subject="local-caller",
        )


def create_mcp_http_token_verifier(
    token_file: Path | None,
    bridge_token_file: Path | None,
) -> TokenVerifier:
    """Load a dedicated caller token and reject bridge-bearer reuse fail closed."""
    if token_file is None:
        raise McpHttpAuthError("MCP_HTTP_AUTH_NOT_CONFIGURED")

    try:
        caller_token = load_bridge_token(token_file)
    except BridgeTokenError as error:
        raise McpHttpAuthError(_caller_token_error_code(error)) from error

    if bridge_token_file is not None:
        try:
            bridge_token = load_bridge_token(bridge_token_file)
        except BridgeTokenError as error:
            raise McpHttpAuthError("MCP_HTTP_AUTH_SEPARATION_UNVERIFIABLE") from error
        if hmac.compare_digest(
            caller_token.authorization_header,
            bridge_token.authorization_header,
        ):
            raise McpHttpAuthError("MCP_HTTP_AUTH_TOKEN_REUSED")

    return LocalMcpHttpTokenVerifier(caller_token.authorization_header.removeprefix("Bearer "))


def _caller_token_error_code(error: BridgeTokenError) -> str:
    if error.error_code == "TOKEN_NOT_CONFIGURED":
        return "MCP_HTTP_AUTH_NOT_CONFIGURED"
    return "MCP_HTTP_AUTH_CONFIG_INVALID"
