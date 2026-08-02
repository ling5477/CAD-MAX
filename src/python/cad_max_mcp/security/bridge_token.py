"""Bounded, path-redacted loading of the machine-local bridge token."""

from __future__ import annotations

import base64
import binascii
import json
import os
import stat
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
        raw = _read_secure_token_bytes(token_file)
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


def _read_secure_token_bytes(token_file: Path) -> bytes:
    """Read once from a verified credential handle instead of a checked pathname.

    Windows production uses an exclusive read handle, checks its DACL and owner through
    that same handle, rejects reparse points, and compares stable identity before and
    after the bounded read. The POSIX fallback uses the equivalent no-follow descriptor
    pattern so hosted Python tests remain fail-closed as well.
    """
    if os.name == "nt":
        return _read_secure_windows_token_bytes(token_file)
    return _read_secure_posix_token_bytes(token_file)


def _read_secure_posix_token_bytes(token_file: Path) -> bytes:
    """Read a private regular file through one no-follow POSIX descriptor."""
    no_follow = getattr(os, "O_NOFOLLOW", None)
    get_effective_user_id = getattr(os, "geteuid", None)
    if no_follow is None or get_effective_user_id is None:
        raise BridgeTokenError("TOKEN_FILE_INSECURE")
    effective_user_id = int(get_effective_user_id())
    descriptor = os.open(
        token_file,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | no_follow,
    )
    try:
        before = os.fstat(descriptor)
        mode = stat.S_IMODE(before.st_mode)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_uid != effective_user_id
            or mode & (stat.S_IRWXG | stat.S_IRWXO)
            or before.st_size <= 0
            or before.st_size > MAX_TOKEN_FILE_BYTES
        ):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")

        raw = os.read(descriptor, MAX_TOKEN_FILE_BYTES + 1)
        after = os.fstat(descriptor)
        if len(raw) != before.st_size or _posix_identity(before) != _posix_identity(after):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")
        return raw
    finally:
        os.close(descriptor)


def _posix_identity(value: os.stat_result) -> tuple[int, int, int, int]:
    """Return the identity and mutation facts that must stay stable across one read."""
    return (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)


def _read_secure_windows_token_bytes(token_file: Path) -> bytes:
    """Open, ACL-check, and read one Windows token object without a pathname race."""
    import ctypes
    from ctypes import wintypes

    class FileTime(ctypes.Structure):
        _fields_ = [("low", wintypes.DWORD), ("high", wintypes.DWORD)]

    class ByHandleFileInformation(ctypes.Structure):
        _fields_ = [
            ("attributes", wintypes.DWORD),
            ("creation_time", FileTime),
            ("last_access_time", FileTime),
            ("last_write_time", FileTime),
            ("volume_serial_number", wintypes.DWORD),
            ("file_size_high", wintypes.DWORD),
            ("file_size_low", wintypes.DWORD),
            ("number_of_links", wintypes.DWORD),
            ("file_index_high", wintypes.DWORD),
            ("file_index_low", wintypes.DWORD),
        ]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    create_file = kernel32.CreateFileW
    create_file.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.LPVOID,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    ]
    create_file.restype = wintypes.HANDLE
    get_file_information = kernel32.GetFileInformationByHandle
    get_file_information.argtypes = [wintypes.HANDLE, ctypes.POINTER(ByHandleFileInformation)]
    get_file_information.restype = wintypes.BOOL
    read_file = kernel32.ReadFile
    read_file.argtypes = [
        wintypes.HANDLE,
        wintypes.LPVOID,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
        wintypes.LPVOID,
    ]
    read_file.restype = wintypes.BOOL
    close_handle = kernel32.CloseHandle
    close_handle.argtypes = [wintypes.HANDLE]
    close_handle.restype = wintypes.BOOL

    generic_read = 0x80000000
    read_control = 0x00020000
    file_share_read = 0x00000001
    open_existing = 3
    file_flag_open_reparse_point = 0x00200000
    invalid_handle_value = ctypes.c_void_p(-1).value
    handle = create_file(
        str(token_file),
        generic_read | read_control,
        file_share_read,
        None,
        open_existing,
        file_flag_open_reparse_point,
        None,
    )
    if handle == invalid_handle_value:
        error_code = ctypes.get_last_error()
        if error_code in {2, 3}:
            raise FileNotFoundError(error_code, "token file is unavailable")
        raise OSError(error_code, "token file is unavailable")

    try:
        before = _windows_file_information(get_file_information, handle, ByHandleFileInformation)
        _validate_windows_file_information(before)
        _assert_secure_windows_acl(handle)

        size = (before.file_size_high << 32) | before.file_size_low
        buffer = (ctypes.c_ubyte * size)()
        bytes_read = wintypes.DWORD()
        if not read_file(handle, buffer, size, ctypes.byref(bytes_read), None):
            raise OSError(ctypes.get_last_error(), "token file is unavailable")
        after = _windows_file_information(get_file_information, handle, ByHandleFileInformation)
        if bytes_read.value != size or _windows_identity(before) != _windows_identity(after):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")
        return bytes(buffer)
    finally:
        close_handle(handle)


def _windows_file_information(
    get_file_information: Any,
    handle: Any,
    information_type: type[Any],
) -> Any:
    """Read stable identity facts from an already-open Windows file handle."""
    import ctypes

    information = information_type()
    if not get_file_information(handle, ctypes.byref(information)):
        raise OSError(ctypes.get_last_error(), "token file is unavailable")
    return information


def _validate_windows_file_information(information: Any) -> None:
    """Reject any non-regular, reparse, linked, empty, or oversized credential object."""
    file_attribute_directory = 0x00000010
    file_attribute_reparse_point = 0x00000400
    size = (information.file_size_high << 32) | information.file_size_low
    if (
        information.attributes & (file_attribute_directory | file_attribute_reparse_point)
        or information.number_of_links != 1
        or size <= 0
        or size > MAX_TOKEN_FILE_BYTES
    ):
        raise BridgeTokenError("TOKEN_FILE_INSECURE")


def _windows_identity(information: Any) -> tuple[int, int, int, int, int]:
    """Return the opened object's identity plus mutation facts for race detection."""
    return (
        information.volume_serial_number,
        information.file_index_high,
        information.file_index_low,
        (information.last_write_time.high << 32) | information.last_write_time.low,
        (information.file_size_high << 32) | information.file_size_low,
    )


def _assert_secure_windows_acl(handle: Any) -> None:
    """Allow only the current user and SYSTEM on the exact opened token object."""
    import ctypes
    from ctypes import wintypes

    class AclSizeInformation(ctypes.Structure):
        _fields_ = [
            ("ace_count", wintypes.DWORD),
            ("acl_bytes_in_use", wintypes.DWORD),
            ("acl_bytes_free", wintypes.DWORD),
        ]

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    get_security_info = advapi32.GetSecurityInfo
    get_security_info.argtypes = [
        wintypes.HANDLE,
        wintypes.DWORD,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.LPVOID),
        ctypes.POINTER(wintypes.LPVOID),
        ctypes.POINTER(wintypes.LPVOID),
        ctypes.POINTER(wintypes.LPVOID),
        ctypes.POINTER(wintypes.LPVOID),
    ]
    get_security_info.restype = wintypes.DWORD
    get_security_descriptor_control = advapi32.GetSecurityDescriptorControl
    get_security_descriptor_control.argtypes = [
        wintypes.LPVOID,
        ctypes.POINTER(wintypes.WORD),
        ctypes.POINTER(wintypes.DWORD),
    ]
    get_security_descriptor_control.restype = wintypes.BOOL
    get_acl_information = advapi32.GetAclInformation
    get_acl_information.argtypes = [
        wintypes.LPVOID,
        wintypes.LPVOID,
        wintypes.DWORD,
        wintypes.DWORD,
    ]
    get_acl_information.restype = wintypes.BOOL
    get_ace = advapi32.GetAce
    get_ace.argtypes = [wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.LPVOID)]
    get_ace.restype = wintypes.BOOL
    is_valid_sid = advapi32.IsValidSid
    is_valid_sid.argtypes = [wintypes.LPVOID]
    is_valid_sid.restype = wintypes.BOOL
    get_length_sid = advapi32.GetLengthSid
    get_length_sid.argtypes = [wintypes.LPVOID]
    get_length_sid.restype = wintypes.DWORD
    equal_sid = advapi32.EqualSid
    equal_sid.argtypes = [wintypes.LPVOID, wintypes.LPVOID]
    equal_sid.restype = wintypes.BOOL
    convert_string_sid = advapi32.ConvertStringSidToSidW
    convert_string_sid.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.LPVOID)]
    convert_string_sid.restype = wintypes.BOOL
    local_free = kernel32.LocalFree
    local_free.argtypes = [wintypes.LPVOID]
    local_free.restype = wintypes.LPVOID

    current_user = _current_windows_user_sid()
    system = wintypes.LPVOID()
    owner = wintypes.LPVOID()
    dacl = wintypes.LPVOID()
    security_descriptor = wintypes.LPVOID()
    if not convert_string_sid("S-1-5-18", ctypes.byref(system)):
        raise OSError(ctypes.get_last_error(), "token ACL is unavailable")
    try:
        owner_security_information = 0x00000001
        dacl_security_information = 0x00000004
        se_file_object = 1
        if (
            get_security_info(
                handle,
                se_file_object,
                owner_security_information | dacl_security_information,
                ctypes.byref(owner),
                None,
                ctypes.byref(dacl),
                None,
                ctypes.byref(security_descriptor),
            )
            != 0
        ):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")
        if not owner or not dacl or not equal_sid(owner, current_user):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")

        control = wintypes.WORD()
        revision = wintypes.DWORD()
        se_dacl_protected = 0x1000
        if (
            not get_security_descriptor_control(
                security_descriptor,
                ctypes.byref(control),
                ctypes.byref(revision),
            )
            or not control.value & se_dacl_protected
        ):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")

        acl_information = AclSizeInformation()
        acl_size_information = 2
        if not get_acl_information(
            dacl,
            ctypes.byref(acl_information),
            ctypes.sizeof(acl_information),
            acl_size_information,
        ):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")

        current_user_read = False
        current_user_write = False
        system_read = False
        access_allowed_ace_type = 0x00
        inherited_ace = 0x10
        read_rights = 0x00000001 | 0x00020089
        write_rights = 0x00000002 | 0x00000116
        for index in range(acl_information.ace_count):
            ace = wintypes.LPVOID()
            if not get_ace(dacl, index, ctypes.byref(ace)):
                raise BridgeTokenError("TOKEN_FILE_INSECURE")
            pointer = ctypes.cast(ace, ctypes.c_void_p).value
            if pointer is None:
                raise BridgeTokenError("TOKEN_FILE_INSECURE")
            ace_type = ctypes.c_ubyte.from_address(pointer).value
            ace_flags = ctypes.c_ubyte.from_address(pointer + 1).value
            ace_size = ctypes.c_ushort.from_address(pointer + 2).value
            if ace_type != access_allowed_ace_type or ace_flags & inherited_ace or ace_size < 16:
                raise BridgeTokenError("TOKEN_FILE_INSECURE")
            rights = ctypes.c_uint32.from_address(pointer + 4).value
            sid = wintypes.LPVOID(pointer + 8)
            if (
                not is_valid_sid(sid)
                or get_length_sid(sid) > ace_size - 8
                or not (equal_sid(sid, current_user) or equal_sid(sid, system))
            ):
                raise BridgeTokenError("TOKEN_FILE_INSECURE")
            if equal_sid(sid, current_user):
                current_user_read |= bool(rights & read_rights)
                current_user_write |= bool(rights & write_rights)
            else:
                system_read |= bool(rights & read_rights)
        if not (current_user_read and current_user_write and system_read):
            raise BridgeTokenError("TOKEN_FILE_INSECURE")
    finally:
        if security_descriptor:
            local_free(security_descriptor)
        if system:
            local_free(system)
        local_free(current_user)


def _current_windows_user_sid() -> Any:
    """Return a LocalAlloc SID for the current process user without naming the user."""
    import ctypes
    from ctypes import wintypes

    class SidAndAttributes(ctypes.Structure):
        _fields_ = [("sid", wintypes.LPVOID), ("attributes", wintypes.DWORD)]

    class TokenUser(ctypes.Structure):
        _fields_ = [("user", SidAndAttributes)]

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    open_process_token = advapi32.OpenProcessToken
    open_process_token.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    open_process_token.restype = wintypes.BOOL
    get_token_information = advapi32.GetTokenInformation
    get_token_information.argtypes = [
        wintypes.HANDLE,
        wintypes.DWORD,
        wintypes.LPVOID,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
    ]
    get_token_information.restype = wintypes.BOOL
    convert_sid_to_string = advapi32.ConvertSidToStringSidW
    convert_sid_to_string.argtypes = [wintypes.LPVOID, ctypes.POINTER(wintypes.LPVOID)]
    convert_sid_to_string.restype = wintypes.BOOL
    convert_string_sid = advapi32.ConvertStringSidToSidW
    convert_string_sid.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.LPVOID)]
    convert_string_sid.restype = wintypes.BOOL
    local_free = kernel32.LocalFree
    local_free.argtypes = [wintypes.LPVOID]
    local_free.restype = wintypes.LPVOID
    close_handle = kernel32.CloseHandle
    close_handle.argtypes = [wintypes.HANDLE]
    close_handle.restype = wintypes.BOOL
    get_current_process = kernel32.GetCurrentProcess
    get_current_process.restype = wintypes.HANDLE

    token = wintypes.HANDLE()
    token_query = 0x0008
    token_user = 1
    required = wintypes.DWORD()
    if not open_process_token(get_current_process(), token_query, ctypes.byref(token)):
        raise OSError(ctypes.get_last_error(), "current user is unavailable")
    try:
        get_token_information(token, token_user, None, 0, ctypes.byref(required))
        if ctypes.get_last_error() != 122 or required.value == 0:
            raise OSError(ctypes.get_last_error(), "current user is unavailable")
        buffer = ctypes.create_string_buffer(required.value)
        if not get_token_information(
            token,
            token_user,
            buffer,
            required.value,
            ctypes.byref(required),
        ):
            raise OSError(ctypes.get_last_error(), "current user is unavailable")
        user = ctypes.cast(buffer, ctypes.POINTER(TokenUser)).contents.user
        if not user.sid:
            raise BridgeTokenError("TOKEN_FILE_INSECURE")
        sid_as_text = wintypes.LPVOID()
        if not convert_sid_to_string(user.sid, ctypes.byref(sid_as_text)):
            raise OSError(ctypes.get_last_error(), "current user is unavailable")
        try:
            current_user = wintypes.LPVOID()
            if not convert_string_sid(
                ctypes.wstring_at(sid_as_text),
                ctypes.byref(current_user),
            ):
                raise OSError(ctypes.get_last_error(), "current user is unavailable")
            return current_user
        finally:
            local_free(sid_as_text)
    finally:
        close_handle(token)


def _is_rfc3339(value: str) -> bool:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return parsed.tzinfo is not None
