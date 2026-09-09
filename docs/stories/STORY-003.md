# STORY-003 — Generate Strong Passwords

As a user, I want to generate strong passwords, so that my accounts are secure.

**Release:** r1 · Google Drive Synchronization (weeks 5–8)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-004** (Functional, must) — The system must generate strong passwords for user credentials.

## How to build it

Implement password generation logic in the vault service, ensuring it meets strength criteria and logs generation events.

## Failure paths you must handle

- Password generation fails due to service error
- User requests unsupported password length
- Logging service is unavailable

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user is logged into the vault, when they request a new password, then a strong password is generated.
- [ ] Given a user requests a password of specific length, when the length is invalid, then an error message is displayed.
- [ ] Trust: Generated passwords are logged with a timestamp for audit purposes.

When every box above is ticked, stop and show the demo.
