# STORY-004 — Lock and Unlock Vault

As a user, I want to lock and unlock my vault, so that my credentials are secure when not in use.

**Release:** r1 · Google Drive Synchronization (weeks 5–8)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-006** (Functional, must) — The system must lock and unlock the vault, rejecting incorrect passwords.
- **REQ-007** (Functional, must) — The system must persist encrypted vault data locally without data loss on close/reopen.

## How to build it

Ensure vault data is encrypted and persists locally. Implement lock/unlock functionality.

## Failure paths you must handle

- User forgets the master password.
- Data corruption occurs during encryption.
- Application crashes during vault unlock.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user has locked the vault, when they unlock it, then all credentials should be accessible.
- [ ] Given a user closes the application, when they reopen it, then the vault should be in a locked state.
- [ ] Trust: Vault data is encrypted and persists without loss on close/reopen.

When every box above is ticked, stop and show the demo.
