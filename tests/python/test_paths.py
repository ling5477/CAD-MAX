"""Allowed-root policy regression tests."""

from __future__ import annotations

from pathlib import Path

import pytest

from cad_max_mcp.security import PathNotAllowedError, PathPolicy


def test_path_inside_allowed_root_is_authorized(tmp_path: Path) -> None:
    """Canonical descendants of an allowed root are accepted."""
    root = tmp_path / "drawings"
    target = root / "part.dwg"
    policy = PathPolicy([root])

    assert policy.authorize(target) == target.resolve(strict=False)


def test_path_outside_allowed_root_is_rejected(tmp_path: Path) -> None:
    """A canonical path outside every root fails closed."""
    policy = PathPolicy([tmp_path / "drawings"])

    with pytest.raises(PathNotAllowedError, match="outside"):
        policy.authorize(tmp_path / "other" / "part.dwg")


def test_path_traversal_is_rejected_before_normalization(tmp_path: Path) -> None:
    """An explicit parent segment is rejected even before containment checking."""
    root = tmp_path / "drawings"
    policy = PathPolicy([root])
    traversal = root / ".." / "secrets" / "part.dwg"

    with pytest.raises(PathNotAllowedError, match="traversal"):
        policy.authorize(traversal)


def test_relative_path_is_rejected(tmp_path: Path) -> None:
    """Relative file input cannot inherit authority from cwd."""
    policy = PathPolicy([tmp_path])

    with pytest.raises(PathNotAllowedError, match="absolute"):
        policy.authorize(Path("part.dwg"))
