# STORY-006 — Recover Vault with Recovery Key

As a user, I want to recover my vault using a recovery key, so that I can regain access if I forget my master password.

**Release:** r1 · Google Drive Synchronization (weeks 5–8)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-009** (Functional, must) — The system must support vault recovery using the recovery key to establish a new master password.

## How to build it

Implement recovery logic that verifies the recovery key and logs the attempt.

## Failure paths you must handle

- Invalid recovery key
- Recovery key expired
- Logging service is unavailable

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user has a valid recovery key, when they use it, then access to the vault is restored.
- [ ] Given a user enters an invalid recovery key, when attempting recovery, then an error message is displayed.
- [ ] Trust: Recovery attempts are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
