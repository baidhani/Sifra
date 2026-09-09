# STORY-002 — Add, View, Edit, Delete, and Search Credentials

As a user, I want to manage my credentials, so that I can keep my information up-to-date and secure.

**Release:** r1 · Google Drive Synchronization (weeks 5–8)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-003** (Functional, must) — The system must support adding, viewing, editing, deleting, and searching login credentials.
- **REQ-005** (Functional, must) — The system must allow manual copying of usernames and passwords.

## How to build it

Implement the UI for credential management, ensuring copy functionality is available for usernames and passwords.

## Failure paths you must handle

- User attempts to copy a non-existent credential.
- Clipboard access is denied by the system.
- User tries to copy while offline.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user is logged in, when they add a new credential, then it should appear in the list.
- [ ] Given a user is viewing a credential, when they choose to copy the username or password, then it should be copied to the clipboard.
- [ ] Trust: All actions are logged with a timestamp and user ID.

When every box above is ticked, stop and show the demo.
