"""Canonical allowed-root policy for future file operations."""

from __future__ import annotations

from pathlib import Path


class PathNotAllowedError(ValueError):
    """Raised when a path fails the configured allowed-root policy."""


class PathPolicy:
    """Authorize canonical absolute paths against canonical allowed roots."""

    def __init__(self, allowed_roots: list[Path]) -> None:
        self._allowed_roots = tuple(root.resolve(strict=False) for root in allowed_roots)

    @property
    def allowed_root_count(self) -> int:
        """Return the number of configured roots without exposing their values."""
        return len(self._allowed_roots)

    def authorize(self, candidate: Path) -> Path:
        """Return the canonical path when authorized, otherwise fail closed."""
        if not candidate.is_absolute():
            raise PathNotAllowedError("Path must be absolute")
        if ".." in candidate.parts:
            raise PathNotAllowedError("Path traversal is not allowed")

        resolved = candidate.resolve(strict=False)
        if not any(resolved.is_relative_to(root) for root in self._allowed_roots):
            raise PathNotAllowedError("Path is outside allowed roots")
        return resolved
