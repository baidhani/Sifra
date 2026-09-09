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
  - Verification: `dotnet test` — 3/3 passed. Manual CLI demo against an isolated temp `APPDATA` confirmed both the vault-creation and already-exists flows.
  - Notes: Stack changed to C#/.NET 10 per user request (originally proposed Node/TypeScript). No cloud or network code touched, so REQ-012 is not at risk in this story.
