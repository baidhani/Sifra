# STORY-009 — Manage Device Identity and Revocation

As a user, I want to manage my devices, so that I can control access to my vault.

**Release:** r3 · Device Management and Browser Integration (weeks 13–16)
**Owner:** User
**Blocked by:** STORY-008

## The requirement this satisfies

- **REQ-015** (Functional, should) — The system must support device identity and revocation.

## How to build it

Implement device identity management and revocation logic.

## Failure paths you must handle

- Revocation fails to block access
- Re-enrollment fails
- Device identity is spoofed

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a device, when it is revoked, then it cannot access the vault.
- [ ] Given a device, when it is re-enrolled, then it regains access.
- [ ] Trust: Device management actions are logged.

When every box above is ticked, stop and show the demo.
