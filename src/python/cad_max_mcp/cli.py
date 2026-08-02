"""Command-line interface for doctor and MCP transports."""

from __future__ import annotations

import argparse
import asyncio
import json
from collections.abc import Sequence
from typing import NoReturn

from pydantic import ValidationError

from cad_max_mcp import SCHEMA_VERSION, SERVER_NAME, __version__
from cad_max_mcp.backends import AutoCadBridgeBackend
from cad_max_mcp.bridge import create_backend
from cad_max_mcp.config import CadMaxSettings
from cad_max_mcp.logging import configure_logging
from cad_max_mcp.security.mcp_http_auth import McpHttpAuthError
from cad_max_mcp.server import TransportName, create_server


def build_parser() -> argparse.ArgumentParser:
    """Build the stable CLI surface."""
    parser = argparse.ArgumentParser(prog="cad-max-mcp")
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("doctor", help="Validate safe base configuration")
    subparsers.add_parser(
        "bridge-doctor",
        help="Validate the authenticated process-level AutoCAD bridge",
    )
    subparsers.add_parser(
        "context-doctor",
        help="Validate AutoCAD main-thread document-context dispatch",
    )
    drawing_doctor = subparsers.add_parser(
        "drawing-doctor",
        help="Validate allowlisted read-only AutoCAD drawing inspection",
    )
    drawing_doctor.add_argument(
        "--expected-active-document-name",
        metavar="BASENAME",
        help="Verify the active document against a safe basename without printing it",
    )

    serve = subparsers.add_parser("serve", help="Start the MCP server")
    serve.add_argument(
        "--transport",
        choices=("stdio", "streamable-http"),
        default="stdio",
    )
    return parser


def run_doctor(settings: CadMaxSettings) -> int:
    """Write sanitized structured diagnostics and return a process exit code."""
    report = {
        "schemaVersion": SCHEMA_VERSION,
        "serverName": SERVER_NAME,
        "serverVersion": __version__,
        "status": "OK",
        "configurationValid": True,
        "httpHost": settings.http_host,
        "httpPort": settings.http_port,
        "httpPath": settings.http_path,
        "bridgeConfigured": settings.bridge_url is not None,
        "bridgeTokenConfigured": settings.bridge_token_file is not None,
        "mcpHttpCallerTokenConfigured": settings.mcp_http_token_file is not None,
        "readOnly": settings.read_only,
        "allowWrite": settings.allow_write,
        "allowScript": settings.allow_script,
        "allowedRootsCount": len(settings.allowed_roots),
    }
    print(json.dumps(report, separators=(",", ":"), sort_keys=True))
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    """Run a CLI command without swallowing configuration failures."""
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        settings = CadMaxSettings()
    except ValidationError:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "serverName": SERVER_NAME,
                    "status": "INVALID_ARGUMENT",
                    "configurationValid": False,
                    "message": "CAD-MAX configuration is invalid",
                },
                separators=(",", ":"),
                sort_keys=True,
            )
        )
        return 2

    if args.command == "doctor":
        return run_doctor(settings)

    if args.command in {"bridge-doctor", "context-doctor", "drawing-doctor"}:
        backend = create_backend(settings)
        if not isinstance(backend, AutoCadBridgeBackend):
            print(
                json.dumps(
                    {
                        "schemaVersion": SCHEMA_VERSION,
                        "status": "BACKEND_NOT_CONFIGURED",
                        "connectionState": "NOT_CONFIGURED",
                        "connected": False,
                        "readOnly": True,
                        "allowWrite": False,
                        "allowScript": False,
                        "documentAccess": False,
                        "mainThreadVerified": False,
                        "dwgRead": False,
                        "dwgWrite": False,
                    },
                    separators=(",", ":"),
                    sort_keys=True,
                )
            )
            return 2
        operation = (
            backend.drawing_doctor(args.expected_active_document_name)
            if args.command == "drawing-doctor"
            else {
                "bridge-doctor": backend.bridge_doctor,
                "context-doctor": backend.context_doctor,
            }[args.command]()
        )
        exit_code, report = asyncio.run(operation)
        print(json.dumps(report, separators=(",", ":"), sort_keys=True))
        return exit_code

    configure_logging()
    transport: TransportName = args.transport
    backend = create_backend(settings)
    try:
        server = create_server(settings, backend, transport)
    except McpHttpAuthError as error:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "serverName": SERVER_NAME,
                    "status": error.error_code,
                    "configurationValid": False,
                    "message": "Streamable HTTP caller authentication is unavailable",
                },
                separators=(",", ":"),
                sort_keys=True,
            )
        )
        return 2
    server.run()
    return 0


def entrypoint() -> NoReturn:
    """Console-script entry point."""
    raise SystemExit(main())
