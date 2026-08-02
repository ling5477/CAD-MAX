# Security model

## Defaults

- readOnly: true
- allowWrite: false
- allowScript: false
- httpHost: 127.0.0.1
- bridge URL: unset
- allowedRoots: empty

These defaults grant no DWG file authority and no mutation authority.

## Network exposure

The Python Streamable HTTP server and C# development Host accept loopback bindings only.
Binding to 0.0.0.0, a LAN address, a public address, or a tunnel is outside this phase and is
rejected or explicitly undocumented. Streamable HTTP additionally requires a separate
machine-local caller bearer token in `CAD_MAX_MCP_HTTP_TOKEN_FILE`; CAD-MAX verifies it with a
constant-time comparison before MCP initialization or tool dispatch. This caller token is
separate from the Python-to-bridge bearer token and is never forwarded to the C# bridge.

Loopback alone is not an authentication boundary against other local processes. The static
caller token reduces that risk for the current Streamable HTTP endpoint, but it does not prove
the identity of the process hosting the C# bridge; that known limitation must be resolved before
any write or script capability. A later remote design requires authentication, authorization,
origin/host validation, rate limits, audit events, and a separate threat model.

Phase 1.4 candidate review retains this server-identity gap as an explicitly accepted P3 only
while the bridge is loopback-only, authenticated, read-only, and unable to run write or script
operations. Any escalation to P0/P1/P2, or any proposal to enable write/script, blocks release
until a reviewed transport identity design is in place.

Both bridge and Streamable HTTP caller credentials are loaded only from one bounded regular-file
handle after the active user's ownership, protected allowlisted ACL, no-reparse policy, and stable
file identity have been checked. Inherited, unresolved, or non-allowlisted access entries fail
closed; token contents, paths, user identities, and ACL details are not emitted to clients or logs.

## Path allowlist

Future path-taking operations must:

1. Require an absolute path.
2. Reject explicit parent traversal segments.
3. Canonicalize both the candidate and configured roots.
4. Prove the candidate is the root or its descendant.
5. Fail with PATH_NOT_ALLOWED otherwise.

An empty allowlist denies every candidate. Paths are never written to client error
messages or routine logs.

## Tool registration

Only cad_system and drawing are registered. When a real accepted bridge advertises the
corresponding dynamic capabilities, drawing supports only status, list_documents,
active_document, units, bounds, layouts, and system_metadata. Write and script tools
are absent, not hidden behind a success stub. Unknown bridge commands return
NOT_IMPLEMENTED.

## Error and log hygiene

Client errors contain stable statuses and safe messages. They do not contain:

- stack traces;
- exception messages from HTTP, AutoCAD, or the operating system;
- full local paths;
- environment variables;
- tokens, cookies, passwords, Autodesk licensing data, or drawings.

Logs use requestId, traceId, command name, and exception type where useful. Do not add
raw request/response bodies to logs.

## Autodesk assets

Never commit Autodesk DLLs, SDK files, installers, license data, or extracted product
content. Local references belong in ignored machine-local configuration.

## Future write gate

Every future mutation requires all of:

- a real handler and failure tests;
- readOnly false;
- allowWrite true;
- an allowed command classification;
- an authorized document and path;
- AutoCAD document-context marshaling;
- a bounded transaction and Undo mark;
- cancellation and timeout behavior;
- structured audit data without sensitive content.
