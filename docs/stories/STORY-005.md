# STORY-005 — Change Master Password

As a user, I want to change my master password, so that I can maintain account security.

**Release:** r1 · Google Drive Synchronization (weeks 5–8)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-008** (Functional, must) — The system must allow changing the master password without re-encrypting all secrets.

## How to build it

Ensure password change functionality updates the master password securely and logs the event.

## Failure paths you must handle

- Incorrect current password
- New password does not meet security criteria
- Logging service is unavailable

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user is logged in, when they change their master password, then the password is updated successfully.
- [ ] Given a user enters an incorrect current password, when attempting to change it, then an error message is displayed.
- [ ] Trust: Password change events are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
