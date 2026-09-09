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
