# STORY-013 — Separate Cloud-Storage Authentication from Vault Authentication

As a user, I want separate authentication for cloud storage and vault access, so that my vault remains secure even if cloud credentials are compromised.

**Release:** r0 · Minimum Viable Vault Core (weeks 1–4)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-012** (Safety, must) — The system must separate cloud-storage authentication from vault authentication.

## How to build it

Implement separate authentication mechanisms for cloud storage and vault access.

## Failure paths you must handle

- User attempts to access the vault without cloud authentication.
- Cloud authentication fails but vault access is needed.
- User forgets vault authentication credentials.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user is logged into cloud storage, when they access the vault, then they must authenticate separately for the vault.
- [ ] Given a user logs out of cloud storage, when they access the vault, then they must authenticate for the vault.
- [ ] Trust: Vault access is logged independently of cloud storage access.

When every box above is ticked, stop and show the demo.
