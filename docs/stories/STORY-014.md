# STORY-014 — Support Local-First Operation with Offline Usability

As a user, I want to use my vault offline, so that I can access my credentials without internet access.

**Release:** r0 · Minimum Viable Vault Core (weeks 1–4)
**Owner:** User
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-014** (Functional, must) — The system must support local-first operation with offline usability.

## How to build it

Ensure vault operations are fully functional offline and changes are queued for later sync.

## Failure paths you must handle

- User tries to sync while offline.
- Data conflicts occur during offline edits.
- User loses data due to failed offline operations.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user is offline, when they access the vault, then they should be able to view and edit credentials.
- [ ] Given a user makes changes offline, when they reconnect, then changes should sync with the cloud.
- [ ] Trust: Offline changes are logged and queued for sync.

When every box above is ticked, stop and show the demo.
