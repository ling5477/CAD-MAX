"""MCP registration smoke test."""

from __future__ import annotations

from cad_max_mcp.backends import NullCadBackend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.server import create_server


async def test_only_implemented_tools_are_registered() -> None:
    """The bootstrap exports exactly cad_system and read-only drawing."""
    server = create_server(CadMaxSettings(), NullCadBackend())

    tools = await server.list_tools()
    names = {tool.name for tool in tools}

    assert names == {"cad_system", "drawing"}
