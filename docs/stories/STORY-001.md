# STORY-001 — Create Vault and Generate Recovery Key

As a user, I want to create a vault and generate a recovery key, so that I can securely store my credentials.

**Release:** r0 · Minimum Viable Vault Core (weeks 1–4)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-001** (Functional, must) — The system must allow a user to create a vault with a master password.
- **REQ-002** (Functional, must) — The system must generate a one-time recovery key during vault creation.

## How to build it

Implement vault creation and recovery key generation using local storage.

## Failure paths you must handle

- Vault creation fails due to storage error
- Recovery key generation fails
- User loses recovery key immediately

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a new user, when they create a vault, then a recovery key is generated.
- [ ] Given a user with a vault, when they attempt to create another vault, then they are prompted to use the existing vault.
- [ ] Trust: The recovery key is securely generated and stored only on the user's device.

When every box above is ticked, stop and show the demo.
