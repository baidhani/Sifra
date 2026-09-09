# STORY-012 — Secure Sharing of Credentials

As a user, I want to share credentials securely, so that I can collaborate with others.

**Release:** r4 · Security Insights and Secure Sharing (weeks 17–20)
**Owner:** User
**Blocked by:** STORY-011

## The requirement this satisfies

- **REQ-017** (Functional, should) — The system must provide secure sharing of credentials and vaults.

## How to build it

Implement secure sharing mechanism for credentials.

## Failure paths you must handle

- Sharing fails to encrypt credentials
- Revocation fails to remove access
- Shared credentials exposed to unauthorized users

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a credential, when shared, then the recipient receives an encrypted copy.
- [ ] Given a shared credential, when revoked, then the recipient loses access.
- [ ] Trust: Sharing actions are logged.

When every box above is ticked, stop and show the demo.
