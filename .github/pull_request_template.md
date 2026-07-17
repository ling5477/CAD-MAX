## Scope

- Goal:
- Files/modules:
- Explicitly excluded:

## Risk and safety

- [ ] No Autodesk proprietary DLL/SDK, credential, `.env`, local path, cache, or build output is included.
- [ ] Phase 0 safety remains `readOnly=true`, `allowWrite=false`, `allowScript=false`, loopback-only.
- [ ] The change does not claim unimplemented AutoCAD/DWG capabilities.
- [ ] High-risk CI, protocol, network, plugin, or future write changes include impact and rollback.

## Documentation budget

- [ ] Current authority was read before implementation.
- [ ] Docs were not changed, or each doc change is required by a fact/entry/Phase transition.
- [ ] `TESTING.md` / `WORKLOG.md` entries are append-only and distinguish PASS, FAIL, and NOT_RUN.

## Validation

- [ ] `./scripts/verify.ps1`
- [ ] `./scripts/docs/verify-docs.ps1`
- [ ] Relevant failure and boundary tests
- [ ] Exact-head GitHub Actions result linked below

Validation evidence:

## Rollback

Revert command or file-level rollback:
