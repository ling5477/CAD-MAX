"""Test-only helpers for creating credential fixtures that satisfy production ACL checks."""

from __future__ import annotations

import json
import os
import stat
import subprocess
from pathlib import Path
from typing import Any


def write_secure_token_file(path: Path, payload: dict[str, Any]) -> None:
    """Write one test token and lock it to the current user plus SYSTEM on Windows."""
    path.write_text(json.dumps(payload), encoding="utf-8")
    if os.name == "nt":
        current_user = os.environ.get("USERNAME")
        if not current_user:
            raise RuntimeError("TEST_CURRENT_USER_UNAVAILABLE")
        completed = subprocess.run(
            [
                "icacls",
                os.fspath(path),
                "/inheritance:r",
                "/grant:r",
                f"{current_user}:(F)",
                "SYSTEM:(F)",
            ],
            check=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        if completed.returncode != 0:
            raise RuntimeError("TEST_TOKEN_ACL_SETUP_FAILED")
    else:
        path.chmod(stat.S_IRUSR | stat.S_IWUSR)


def grant_insecure_test_reader(path: Path) -> None:
    """Deliberately add a broad reader so tests prove the loader fails closed."""
    if os.name == "nt":
        completed = subprocess.run(
            ["icacls", os.fspath(path), "/grant", "Users:(R)"],
            check=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        if completed.returncode != 0:
            raise RuntimeError("TEST_TOKEN_ACL_MUTATION_FAILED")
    else:
        path.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IRGRP)
