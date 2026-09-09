# STORY-008 — Synchronize Vault with OneDrive and Dropbox

As a user, I want to synchronize my vault with OneDrive and Dropbox, so that I have flexibility in storage providers.

**Release:** r2 · Extended Synchronization Providers (weeks 9–12)
**Owner:** User
**Blocked by:** STORY-007

## The requirement this satisfies

- **REQ-011** (Functional, should) — The system must use Microsoft OneDrive and Dropbox for encrypted cross-device synchronization after Google Drive.

## How to build it

Implement synchronization using provider-specific APIs for OneDrive and Dropbox.

## Failure paths you must handle

- Sync fails with OneDrive
- Sync fails with Dropbox
- Data corruption during sync

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a vault, when synchronized with OneDrive, then data is encrypted and uploaded.
- [ ] Given a vault, when synchronized with Dropbox, then data is encrypted and uploaded.
- [ ] Trust: Synchronization actions are logged for both providers.

When every box above is ticked, stop and show the demo.
