# Sifra — Stories

15 stories across 5 releases, walking-skeleton first:
the earliest release proves the thinnest end-to-end path including the trust
spine, and later releases stack features on top of something already working.

## Before the releases — start here

- **[STORY-000](stories/STORY-000.md)** — Build your Command Center

The first thing you build, on day one, before any part of the system itself. It is
the page you keep open for the rest of the programme and demo from. It belongs to no
release and fulfils none of your requirements, because it is the window onto your
system rather than a part of it.

## r0 · Minimum Viable Vault Core — weeks 1–4

**Goal:** Establish a standalone encrypted vault for one user on one device.
**Done when you can show:** Create a vault, generate a recovery key, add a credential, lock, unlock, read/edit credential, close, reopen, unlock, verify persistence.

- **[STORY-001](stories/STORY-001.md)** — Create Vault and Generate Recovery Key
- **[STORY-013](stories/STORY-013.md)** — Separate Cloud-Storage Authentication from Vault Authentication
- **[STORY-014](stories/STORY-014.md)** — Support Local-First Operation with Offline Usability
- **[STORY-015](stories/STORY-015.md)** — Establish Trust Spine for Vault Operations

## r1 · Google Drive Synchronization — weeks 5–8

**Goal:** Enable encrypted cross-device synchronization using Google Drive.
**Done when you can show:** Synchronize vault data across devices using Google Drive, maintaining encryption and user ownership.

- **[STORY-002](stories/STORY-002.md)** — Add, View, Edit, Delete, and Search Credentials
- **[STORY-003](stories/STORY-003.md)** — Generate Strong Passwords
- **[STORY-004](stories/STORY-004.md)** — Lock and Unlock Vault
- **[STORY-005](stories/STORY-005.md)** — Change Master Password
- **[STORY-006](stories/STORY-006.md)** — Recover Vault with Recovery Key
- **[STORY-007](stories/STORY-007.md)** — Synchronize Vault with Google Drive _(waits on STORY-006)_

## r2 · Extended Synchronization Providers — weeks 9–12

**Goal:** Add support for Microsoft OneDrive and Dropbox synchronization.
**Done when you can show:** Synchronize vault data using OneDrive and Dropbox, maintaining encryption and user ownership.

- **[STORY-008](stories/STORY-008.md)** — Synchronize Vault with OneDrive and Dropbox _(waits on STORY-007)_

## r3 · Device Management and Browser Integration — weeks 13–16

**Goal:** Implement device identity, revocation, and browser integration.
**Done when you can show:** Manage device identities and perform browser-based credential autofill.

- **[STORY-009](stories/STORY-009.md)** — Manage Device Identity and Revocation _(waits on STORY-008)_
- **[STORY-010](stories/STORY-010.md)** — Browser Integration for Credential Autofill _(waits on STORY-009)_

## r4 · Security Insights and Secure Sharing — weeks 17–20

**Goal:** Introduce password health analysis, breach awareness, and secure sharing.
**Done when you can show:** Analyze password health, detect breaches, and share credentials securely.

- **[STORY-011](stories/STORY-011.md)** — Analyze Password Health and Breach Awareness _(waits on STORY-010)_
- **[STORY-012](stories/STORY-012.md)** — Secure Sharing of Credentials _(waits on STORY-011)_
