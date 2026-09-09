# Sifra — Requirements

A secure, user-owned password vault evolving into a cross-platform, zero-knowledge digital vault and personal security platform.

This is the source of truth for what you are building. Your Claude Code prompts
point here. If you sharpen a requirement, edit it — your version is the real one.

| Kind | Meaning |
|---|---|
| Functional | something the system does |
| Safety | a guardrail, with a check that enforces it |
| Reliability | how it behaves when something fails |
| Constraint | a technology or vendor you must use — context, not a task |

## Browser Integration

### REQ-016 — Functional · should

The system must support browser integration for credential discovery and autofill.

Fulfilled by: STORY-010

## Device Management

### REQ-015 — Functional · should

The system must support device identity and revocation.

Fulfilled by: STORY-009

## Local Operation

### REQ-014 — Functional · must

The system must support local-first operation with offline usability.

Fulfilled by: STORY-014

## Secure Sharing

### REQ-017 — Functional · should

The system must provide secure sharing of credentials and vaults.

Fulfilled by: STORY-012

## Security

### REQ-012 — Safety · must

The system must separate cloud-storage authentication from vault authentication.

Fulfilled by: STORY-013

### REQ-013 — Constraint

The system must use established cryptographic libraries and primitives for encryption.

Context for the stories that use it — constraints do not get their own story.

## Security Insights

### REQ-018 — Functional · should

The system must support password health analysis and breach awareness.

Fulfilled by: STORY-011

## Synchronization

### REQ-010 — Functional · must

The system must use Google Drive for encrypted cross-device synchronization.

Fulfilled by: STORY-007

### REQ-011 — Functional · should

The system must use Microsoft OneDrive and Dropbox for encrypted cross-device synchronization after Google Drive.

Fulfilled by: STORY-008

## Vault Core

### REQ-001 — Functional · must

The system must allow a user to create a vault with a master password.

Fulfilled by: STORY-001, STORY-015

### REQ-002 — Functional · must

The system must generate a one-time recovery key during vault creation.

Fulfilled by: STORY-001, STORY-015

### REQ-003 — Functional · must

The system must support adding, viewing, editing, deleting, and searching login credentials.

Fulfilled by: STORY-002

### REQ-004 — Functional · must

The system must generate strong passwords for user credentials.

Fulfilled by: STORY-003

### REQ-005 — Functional · must

The system must allow manual copying of usernames and passwords.

Fulfilled by: STORY-002

### REQ-006 — Functional · must

The system must lock and unlock the vault, rejecting incorrect passwords.

Fulfilled by: STORY-004

### REQ-007 — Functional · must

The system must persist encrypted vault data locally without data loss on close/reopen.

Fulfilled by: STORY-004

### REQ-008 — Functional · must

The system must allow changing the master password without re-encrypting all secrets.

Fulfilled by: STORY-005, STORY-015

### REQ-009 — Functional · must

The system must support vault recovery using the recovery key to establish a new master password.

Fulfilled by: STORY-006, STORY-015
