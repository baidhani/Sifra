# Progress

- [x] STORY-000: build the Command Center
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Built the Sifra Command Center (`index.html` at repo root) with all 9 tabs reading `.colaberry/plan.json`, `progress.json`, `manifest.json` at runtime; added `.nojekyll` so GitHub Pages serves the `.colaberry/` data files; registered the push webhook; enabled GitHub Pages.
  - Verification: All 5 STORY-000 acceptance criteria ticked in `.colaberry/progress.json` and confirmed verified in the portal; published site returns 200 for `index.html` and all three data files.
  - Notes: Requirement→story `fulfilled_by` mapping in the initial `plan.json` was inferred from title matching (flagged to user as an assumption).

- [x] STORY-001: create vault and generate recovery key
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Added `Sifra.Vault` (.NET 10 class library) with `VaultStore` (atomic local file storage), `RecoveryKeyGenerator` (160-bit crypto-random, Base32), and `VaultService.CreateVault()` (persists only a salted PBKDF2-SHA256 hash of the recovery key, throws `VaultAlreadyExistsException` on a second call). Added `Sifra.Cli` as a thin console demo. Added 3 xUnit tests (happy path, already-exists path, storage-error failure path).
  - Verification: `dotnet test` — 3/3 passed. Manual CLI demo confirmed both the vault-creation and already-exists flows.
  - Notes: Stack changed to C#/.NET 10 per user request (originally proposed Node/TypeScript). No cloud or network code touched, so REQ-012 is not at risk in this story. Correction (found during STORY-013): the CLI demo was believed to run against an isolated temp `APPDATA`, but setting the `APPDATA` env var from Git Bash did not actually redirect .NET's `SpecialFolder.ApplicationData` on Windows — it silently wrote real files to the actual user profile. Those stray files were deleted and `Sifra.Cli` was fixed in STORY-013 to accept an explicit data-directory argument instead. The xUnit tests were unaffected — they always used the constructor's `dataDirectory` parameter directly, never the env var.

- [x] STORY-013: separate cloud-storage authentication from vault authentication
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Added `Sifra.Vault.Auth`: `VaultAuthenticator`/`VaultAccessCredentialStore` (own file, own hash, no reference to cloud state at all) and `ICloudAuthProvider` with a local fake implementation (no real cloud provider exists yet — that's STORY-007). Vault and cloud access are logged to two separate files, not a shared log with a tag. Updated `Sifra.Cli` to demo the separation and to take an explicit data-directory argument (see correction above). Added 5 xUnit tests.
  - Verification: `dotnet test` — 8/8 passed (3 from STORY-001 + 5 new). Manual CLI demo against a genuinely isolated temp directory confirmed vault auth succeeds regardless of cloud sign-in/out/failure, and rejects a wrong credential.
  - Notes: Deliberately did not touch any STORY-001 file — the two auth mechanisms share no code or storage. "Vault access credential" is a story-scoped stand-in for the real master password, which belongs to STORY-004/005.

- [x] STORY-014: support local-first operation with offline usability
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Added `Sifra.Vault.Sync`: `VaultDataStore`/`OfflineChangeQueueStore` (local-only, no network on the view/edit path) and `ICloudSyncProvider` with a `LocalFakeCloudSyncProvider` stand-in (no real cloud provider exists yet — that's STORY-007). `VaultDataService.Edit()` persists then queues (deduped per item); `SyncNow()` drains the queue only when connected, throws and leaves the queue intact when offline, and keeps a change queued (not lost) on a reported conflict. Updated `Sifra.Cli` to demo the full offline → queue → reconnect → sync flow. Added 7 xUnit tests.
  - Verification: `dotnet test` — 15/15 passed (8 prior + 7 new). Manual CLI demo confirmed offline edit/view, queued change, sync failure while offline, and successful drain on reconnect.
  - Notes: **Flagged for revisit.** Criteria 1-2 were ticked against a placeholder `VaultDataItem` and the local fake sync provider — not the real `Credential` model (STORY-002) or real cloud sync (STORY-007), neither of which exists yet. This was a deliberate, user-agreed call because the portal gates all of r1 (including STORY-002/007) behind STORY-014 and STORY-015 completing, so leaving these unticked would have blocked the whole build. Re-verify against the real types once STORY-002 and STORY-007 land.

- [x] STORY-015: establish trust spine for vault operations
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Added `Sifra.Vault.Audit`: `AuditLogger` (unique collision-checked operation ids, capped retries on log-write failure, admin alert + clear exception when retries exhaust, truncation instead of silent drop for oversized details), `IAuditLogSink`/`FileAuditLogSink`, `IAdminAlertSink`/`LocalFakeAdminAlertSink` (stand-in — no real notification channel exists). Wired additively into `VaultService.CreateVault()`, `VaultAuthenticator.Authenticate()`, and `VaultDataService.Edit()`/`SyncNow()` via an optional constructor parameter (default null); confirmed every prior test still passes unchanged. Added 11 xUnit tests (7 for AuditLogger in isolation, 4 integration tests proving the wiring fires for real).
  - Verification: `dotnet test` — 26/26 passed (15 prior + 11 new). Manual CLI demo showed 6 real operations logged end-to-end with unique ids/timestamps/user ids, plus a simulated logging-service outage correctly retrying, alerting, and raising a clear exception.
  - Notes: This was the last story in the r0 plan — the portal gates r1 (STORY-002, STORY-007, etc.) behind STORY-014 and STORY-015 both completing. UserId is `Environment.UserName` (no account system exists). Credentials/passwords are never written to the audit log (verified by test). This is the last story in the current plan; next work starts in r1 once the portal unlocks it.

- [x] STORY-002: add, view, edit, delete, and search credentials
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Added `Sifra.Vault.Crypto` (`VaultEncryptionService` — AES-256-GCM, key via PBKDF2 from the same vault credential STORY-013 uses but a separate persisted salt so the two derived outputs are cryptographically independent; `VaultEncryptionKeyStore` persists the salt exactly once, idempotently) and `Sifra.Vault.Credentials` (`Credential`/`CredentialStore` — username/password ciphertext at rest, label/url plaintext metadata; `CredentialService` for add/view/edit/delete/search/copy; `ICredentialClipboard` with a real `TextCopy`-based implementation — first external dependency in this repo — and a fake for tests). Reused the existing `AuditLogger` unmodified. Added 14 xUnit tests.
  - Verification: `dotnet test` — 40/40 passed (26 prior + 14 new). Manual CLI demo confirmed real clipboard copy, raw-file inspection showing ciphertext only (no plaintext username/password), all 3 named failure paths, and all 5 new operations appearing in the trust-spine log.
  - Notes: Encrypting credentials at rest now (rather than plaintext-then-harden) was a deliberate, user-approved decision — flagged as a scope question, user chose "encrypt now." The `TextCopy` dependency was flagged but not separately confirmed (user answered the encryption question directly); worth a quick explicit confirmation. Label/Url left as plaintext metadata is a documented scoping choice, not explicitly confirmed with the user. `CredentialService` does not itself enforce authentication — callers pass the same vault credential already proven via `VaultAuthenticator`; a real session/lock gate is STORY-004/005's job.

- [x] STORY-003: generate strong passwords
  - Date: 2026-09-09
  - Session: CC-20260909-a1k4
  - What changed: Added `Sifra.Vault.Passwords`: `PasswordGenerator` (crypto-random via `RandomNumberGenerator.GetInt32`, guarantees at least one lowercase/uppercase/digit/symbol, Fisher-Yates shuffle), length bounded to [8, 128] via `UnsupportedPasswordLengthException`, random-source failures wrapped as `PasswordGenerationFailedException` through an injectable `IRandomIndexSource` seam. Reused the existing `AuditLogger` unmodified. Added 7 xUnit test methods (10 test cases counting the 4-case length-validation theory).
  - Verification: `dotnet test` — 50/50 passed (40 prior + 10 new). Manual CLI demo confirmed password generation, invalid-length rejection with a clear message, and the generation event appearing in the trust-spine log without the password value.
  - Notes: Deliberate deviation from the literal acceptance text — the audit log records that a password was generated (timestamp, user, length) but never the password value itself, since logging real secrets would violate the no-secrets-in-logs rule governing this project. Flagged explicitly rather than silently doing either the insecure literal thing or the safe thing unremarked.
