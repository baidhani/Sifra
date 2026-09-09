# STORY-010 — Browser Integration for Credential Autofill

As a user, I want browser integration, so that I can autofill credentials on websites.

**Release:** r3 · Device Management and Browser Integration (weeks 13–16)
**Owner:** User
**Blocked by:** STORY-009

## The requirement this satisfies

- **REQ-016** (Functional, should) — The system must support browser integration for credential discovery and autofill.

## How to build it

Implement browser extension for credential discovery and autofill.

## Failure paths you must handle

- Autofill fails on supported sites
- Incorrect credentials filled
- Autofill occurs without user consent

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a browser, when a user visits a login page, then credentials are offered for autofill.
- [ ] Given a browser, when a user denies autofill, then credentials are not filled.
- [ ] Trust: Autofill actions are logged.

When every box above is ticked, stop and show the demo.
