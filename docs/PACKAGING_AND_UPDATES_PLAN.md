# Packaging, Installer, and Auto-Update — Plan & Continuity Point

Last updated: 2026-09-26. This is a **planning document**, not a shipped feature —
nothing described here has been implemented yet. It exists so a future session
(or a different person) can pick this up without re-deriving the discussion
that produced it.

## Why this exists

Sifra currently has no installer and no update-check mechanism. `publish-vm-test/`
is just a raw `dotnet publish` framework-dependent output folder used for manual
copying to a test VM — not a real distribution artifact. Before Sifra goes to any
real user beyond manual testing, both of these need to exist.

## Decisions made (do not re-litigate without a reason)

1. **Update distribution channel: GitHub Releases.** The repo
   (`github.com/baidhani/Sifra`) already exists; releases API
   (`api.github.com/repos/baidhani/Sifra/releases/latest`) gives version +
   asset URLs for free, no server to stand up or maintain.

2. **Update UX flow: check → prompt → download → launch installer.**
   Explicitly NOT silent/automatic patching, and NOT "just link out to a
   download page." Sequence: app checks in the background (startup + a
   manual "Check for updates" button in Options), shows a prompt if a newer
   version exists, downloads the installer .exe to a temp path with
   progress on consent, then launches it and closes Sifra so the installer
   can replace files.

3. **Installer tool: Inno Setup.** Chosen over Velopack and WiX/MSI
   specifically because the update flow above doesn't use silent delta
   patching (Velopack's main advantage), and Inno Setup is simpler to set
   up than WiX for the same "close running app → install → relaunch →
   uninstaller" result. No new .NET dependency — it's a build-time `.iss`
   script, not a NuGet package.

4. **Code signing: Azure Trusted Signing**, not a traditional CA
   certificate. Chosen because it avoids the hardware-token/cloud-HSM
   requirement traditional OV/EV certs now have (a 2023 CA/Browser Forum
   baseline requirement change), costs ~$10/month, and supports
   **Individual** identity verification (not just registered businesses) —
   the right fit for a solo/indie project like Sifra.

## Code signing — status and how to resume it

**User is walking through this themselves; I (Claude) cannot do the
account/identity-verification steps — they require the user's own payment
method and government ID.** As of 2026-09-26, this had NOT yet been started
by the user in this conversation (guidance was given, not yet acted on).

Steps, for whoever resumes this:
1. Create/use an Azure account at azure.microsoft.com (Microsoft account +
   payment method for the subscription).
2. In the Azure Portal, create a **Trusted Signing** resource. It's only
   available in a handful of regions (East US, West US 2, and a few
   others as of when this was written) — the portal restricts the region
   picker to supported ones.
3. Inside it, create a **Certificate Profile** with type **Public Trust**
   (not Private Trust — Private isn't recognized as trusted by Windows/
   SmartScreen) and identity type **Individual**.
4. Complete identity verification (government-issued photo ID, likely a
   live/video verification step via Microsoft's vendor) — this can take a
   few business days, so worth starting early.
5. Once approved: an account name, certificate profile name, and endpoint
   exist. Signing itself happens via `signtool` + the Trusted Signing
   plugin, or the open-source `AzureSignTool`, authenticated through an
   Azure AD app registration with permission to the Trusted Signing
   account.
6. **When the user reaches step 5/6**: they should hand Claude the account
   name / profile name / tenant info via **GitHub Actions secrets, never
   pasted directly into chat**, and Claude wires the actual signing step
   into the release build (signing `Sifra.Desktop.exe`,
   `Sifra.NativeHost.exe`, and the Inno Setup installer output).

## Implementation plan — Phase A (buildable now, does not need signing)

Not started as of 2026-09-26 (planning-only session). When resumed:

1. **`UpdateChecker` service** — likely lives in `Sifra.Vault` (keeps the
   pattern of business logic living there, thin UI wiring in Desktop) or
   `Sifra.Desktop` if it turns out to need WPF-specific things; decide when
   actually building it. Queries GitHub's Releases API, compares the latest
   tag against the running `<Version>` (`Sifra.Desktop.csproj`), exposes
   "update available" + the installer asset's download URL.
2. **Background check on startup** — mirror the existing `SyncScheduler`
   pattern (`src/Sifra.Desktop/SyncScheduler.cs`) for how a periodic
   background check that doesn't block the UI thread is already done in
   this codebase.
3. **Options UI**: a manual "Check for updates" button.
4. **Update-available UI**: a small notice/dialog — "Sifra 0.0.50 is
   available — Download & Install?" On consent: download the installer to
   a temp folder with progress, then launch it and close Sifra.
5. **Inno Setup script** (`.iss`, new file, likely at repo root or a new
   `installer/` folder): defines the installer, handles closing a running
   Sifra instance, installs, includes an uninstaller, and **re-registers
   the native-messaging manifest** (see gotcha below) on both install and
   update.

## A gotcha already identified, not yet solved

`browser-extension/native-host-manifest.json` currently points at a fixed
dev path (`C:\Users\firas\Documents\Sifra-AI-Project\src\Sifra.NativeHost\bin\Debug\net10.0\Sifra.NativeHost.exe`),
registered in `HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.sifra.nativehost`
(see `sifra_extension_findings.md` / this session's earlier native-host work).
A real installer needs to:
- Write the manifest with the **actual install path** (e.g.
  `C:\Program Files\Sifra\Sifra.NativeHost.exe`), not the dev path.
- Re-register it on every update, in case the install path ever changes.
- This should be an Inno Setup install step, not something the app does at
  runtime.

## Phase B (blocked on code signing being ready)

Wire a signing step into whatever builds the release artifacts — sign
`Sifra.Desktop.exe`, `Sifra.NativeHost.exe`, and the Inno Setup installer
output before it's attached to a GitHub Release.

## How to resume this from a cold start

1. Read this file in full.
2. Ask the user: "Did you get anywhere on the Azure Trusted Signing
   enrollment?" — if yes, note the account/profile info request above (via
   secrets, not chat) and do Phase B alongside Phase A. If no, proceed with
   Phase A only; signing is not a blocker for building/testing the
   installer and updater logic unsigned.
3. Start Phase A at whichever numbered step above is unchecked — none are
   checked off as of 2026-09-26.
