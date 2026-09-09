# STORY-011 — Analyze Password Health and Breach Awareness

As a user, I want to analyze my password health, so that I can improve security.

**Release:** r4 · Security Insights and Secure Sharing (weeks 17–20)
**Owner:** User
**Blocked by:** STORY-010

## The requirement this satisfies

- **REQ-018** (Functional, should) — The system must support password health analysis and breach awareness.

## How to build it

Implement local analysis for password health and breach detection.

## Failure paths you must handle

- Analysis fails to identify weak passwords
- False positives in breach detection
- Plaintext passwords exposed during analysis

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a vault, when password health is analyzed, then weak passwords are identified.
- [ ] Given a vault, when breach awareness is enabled, then compromised passwords are flagged.
- [ ] Trust: Analysis results are logged without exposing plaintext passwords.

When every box above is ticked, stop and show the demo.
