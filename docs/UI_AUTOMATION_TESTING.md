# Live UI Automation Testing — How To

This document exists so a future Claude Code session (with no memory of any
prior conversation) can drive the real, running Sifra.Desktop app the same
way it was driven throughout the Phase 3 cloud-sync work: launching it,
clicking through real screens, completing real OAuth sign-ins with the
user's help, and verifying results directly against the database, the
audit log, and the real cloud accounts. Nothing here is a testing
*framework* — it's PowerShell driving Windows UI Automation (the same
accessibility API screen readers use), plus a few Win32 calls for things
UI Automation doesn't reach.

If you're reading this because a task requires "actually running the app
and checking it works" (not just unit tests), this is the playbook.

## Why this exists instead of a screenshot/vision-based approach

Screenshot capture (`CopyFromScreen` from PowerShell) does **not** work
reliably in this environment — it has been observed capturing an unrelated
window (the chat/terminal pane) instead of the target app, even when
`GetForegroundWindow()` confirmed the target *was* the foreground window.
This looks like a session/window-station mismatch in how this agent
environment is set up, not a timing bug — retrying the same capture does
not fix it. **Do not spend time debugging screenshot capture.** UI
Automation tree inspection (below) is the reliable substitute for "seeing"
the app, and it works even when a window is fully occluded or off-screen.

## The toolchain

- **PowerShell** is the driver. Use `Add-Type -AssemblyName UIAutomationClient` and
  `Add-Type -AssemblyName UIAutomationTypes` at the top of any script block that
  touches the UI.
- **Bash** is used only for `dotnet build`/`dotnet run`/`dotnet test` and for
  reading log files after the fact.
- Every PowerShell tool call is a **separate process**. Types defined via
  `Add-Type` in one call do **not** persist to the next call — redefine any
  Win32 helper class you need in every call that uses it, or do everything
  that needs it in one call.

## Launching and stopping the app

```bash
# Build first (always — don't run against a stale binary)
cd "C:/Users/firas/Documents/Sifra-AI-Project"
dotnet build src/Sifra.Desktop

# Launch in background, capturing all output (including crash stack traces) to a log file
nohup dotnet run --project src/Sifra.Desktop --no-build > /tmp/sifra_desktop_run.log 2>&1 &
echo "Launched PID $!"
sleep 3   # give WPF time to start and show the first window
```

```powershell
# Stop it (always do this before rebuilding — a running process locks the DLL and the build will fail with MSB3027)
Get-Process -Name "Sifra.Desktop" -ErrorAction SilentlyContinue | Stop-Process -Force
```

**If the app appears to have vanished** (a UI Automation call can't find
the window, or `Get-Process -Name "Sifra.Desktop"` returns nothing after a
click), it crashed. Read the log file — .NET prints the full unhandled
exception and stack trace there. This is the single most useful diagnostic
step when something "just doesn't respond": check the log before assuming
the automation script did something wrong.

```bash
head -20 /tmp/sifra_desktop_run.log   # or: grep -n "Unhandled exception" -A 20
```

## Finding the window and inspecting what's on screen

```powershell
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$proc = Get-Process -Name "Sifra.Desktop"
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
```

**Always dump the tree before trying to interact with a screen you haven't
seen before** — never guess at AutomationIds or control layout. This is
the reliable substitute for looking at the screen:

```powershell
function Dump-Tree($el, $depth) {
    $indent = "  " * $depth
    Write-Output "$indent[$($el.Current.ControlType.ProgrammaticName)] Name='$($el.Current.Name)' AutomationId='$($el.Current.AutomationId)'"
    if ($depth -lt 6) {
        foreach ($c in $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            Dump-Tree $c ($depth + 1)
        }
    }
}
Dump-Tree $root 0
```

This prints every control, its type, its visible text (`Name`), and its
`AutomationId` (the stable identifier XAML assigns via
`AutomationProperties.AutomationId` or, for named elements, often just
`x:Name`). Read the output before writing the next command — don't assume
what's on screen.

## Interacting with controls

Find an element by its AutomationId (the reliable way — don't match on
`Name`/visible text, it changes with locale/content):

```powershell
function Find-ById($id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
```

**Click a button:**
```powershell
Find-ById "CreateVaultButton" | ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
```

**Type into a TextBox or PasswordBox** (works even on the WPF-UI
`ui:PasswordBox` control used throughout this app, which — unlike a plain
WPF `PasswordBox` — does support `ValuePattern`, confirmed by use
throughout this session):
```powershell
$pwBox = Find-ById "MasterPasswordBox"
$pwBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("some-password")
```

**Select a RadioButton:**
```powershell
Find-ById "SyncProviderGoogleDriveRadio" | ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
```

**Read a list's items** (e.g. the credentials list):
```powershell
$list = Find-ById "CredentialsList"
$items = $list.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output "Item count: $($items.Count)"
```

Always `Start-Sleep -Milliseconds 500-1500` after an action that triggers
navigation, a new window, or async work — UI Automation calls happen
faster than WPF can render/transition, and querying too early returns
stale or missing elements.

## Secondary windows (dialogs, Add Credential, Options, Migrate, etc.)

Clicking a button that opens a new `Window` (not just changing content
inside the current one) creates a **separate top-level window** that is
not a descendant of the main window's `AutomationElement` tree. Find it by
enumerating all visible windows belonging to the process via Win32
`EnumWindows` (much faster and more reliable than enumerating the whole
desktop root through UI Automation, which can take 30+ seconds):

```powershell
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class EnumWinHelper {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public static List<IntPtr> GetWindowsForProcess(uint pid) {
        var result = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            uint wPid;
            GetWindowThreadProcessId(hWnd, out wPid);
            if (wPid == pid && IsWindowVisible(hWnd)) result.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
"@
$proc = Get-Process -Name "Sifra.Desktop"
$handles = [EnumWinHelper]::GetWindowsForProcess([uint32]$proc.Id)
foreach ($h in $handles) {
    $sb = New-Object System.Text.StringBuilder 256
    [EnumWinHelper]::GetWindowText($h, $sb, 256) | Out-Null
    Write-Output "Handle=$h Title='$($sb.ToString())'"
}
```

Give each `EnumWinHelper`-like class a **unique name per script block**
(`EnumWinHelper`, `EnumWinHelper2`, ...) if you're going to redefine it in
multiple calls — re-declaring the exact same type name in the same
PowerShell session throws `Cannot add type ... it already exists`. Since
each tool call is a fresh process this usually isn't an issue, but if you
chain multiple `Add-Type` calls with the same class name inside one script
block, it will fail.

Once you have the handle, build an `AutomationElement` from it exactly the
same way as the main window:
```powershell
$el = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]<handle>)
```
Then `Dump-Tree`, `Find-ById`, `Invoke()`, etc. all work identically
against `$el` instead of `$root`.

## MessageBox dialogs (`System.Windows.MessageBox.Show(...)`)

These are real Win32 dialogs, but their buttons show up in the UI
Automation tree as `ControlType.Pane`, **not** `ControlType.Button` — they
do not support `InvokePattern`, so `GetCurrentPattern(InvokePattern)` will
throw `"Unsupported Pattern."`. You can still **read** the dialog's text
this way (useful for verifying a specific success/error message appeared),
but to dismiss it, bring it to the foreground and send a key instead:

```powershell
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class FgWinHelper { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd); }
"@
[FgWinHelper]::SetForegroundWindow([IntPtr]<messageBoxHandle>) | Out-Null
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")   # activates the default button (usually OK)
```

Note: `MessageBox.Show(owner, text, "Sifra", ...)` shares the exact title
"Sifra" with the main window — when enumerating windows by title you'll
see multiple "Sifra" entries; disambiguate by which one is newest / has a
`Pane` child whose `Name` matches the message text you expect, or just
dump the tree of each candidate handle and look for the message text.

## Reading the app's default (real) master password box, Setup vs Unlock

The app has exactly two "gate" screens before the main vault UI:
- **SetupView** (first run, no vault exists yet): `AutomationId`s include
  `CloudFolderBox`, `SyncProviderNoneRadio`/`SyncProviderDropboxRadio`/`SyncProviderGoogleDriveRadio`,
  `MasterPasswordBox`, `CreateVaultButton`, then after creation:
  `RecoveryKeyText`, `CopyToClipboardButton`, and a "Continue to Unlock" button (no
  AutomationId set on it as of this writing — match by `Name` instead).
- **UnlockView** (every subsequent launch): `MasterPasswordBox`, `UnlockButton`.

**Do not type a real vault's master password into automation scripts.**
If the vault currently open is the user's real data (not a disposable test
vault you created), stop and ask the user to unlock it themselves, then
resume driving the app from the unlocked state.

**These AutomationIds will drift as the app changes.** Treat any ID quoted
in this document as "true as of when this was written" — always `Dump-Tree`
first on whatever screen you're actually looking at rather than trusting a
hardcoded list. A short, current list of the ones already wired (useful as
a starting point, not a guarantee): `MasterPasswordBox`, `UnlockButton`,
`CreateVaultButton`, `RecoveryKeyText`, `CopyToClipboardButton`,
`AddCredentialButton`, `EditCredentialButton`, `DeleteCredentialButton`,
`LockButton`, `SyncButton`, `SyncStatusText`, `SearchBox`, `CredentialsList`,
`AllItemsCategory`/`FavoritesCategory`/`WeakCategory`/`ReusedCategory`/`CompromisedCategory`,
`LabelBox`, `FieldsList`, `SaveCredentialButton`, `AutoLockCombo`,
`MigrateSyncProviderButton`, `SyncProviderStatusText`, `CurrentProviderText`,
`MigrateButton`.

## Testing real cloud provider sign-in (Dropbox / Google Drive)

`DropboxAuthProvider.SignIn()` and `GoogleDriveAuthProvider.SignIn()` open
a **real system browser** via `Process.Start(..., UseShellExecute = true)`
and wait for a real OAuth redirect — there is no way to script around this,
and there should not be: it's a real user consenting to a real account
access grant. When a script triggers one of these (directly, or indirectly
via `AppServices.EnsureSignedIn`/`EnsureActiveProviderSignedIn`):

1. Check which browser window opened and to what site:
   ```powershell
   Get-Process | Where-Object { $_.ProcessName -match "msedge|chrome|firefox|librewolf" -and $_.MainWindowTitle -match "Dropbox|Google|Sign in" } | Select-Object Id, ProcessName, MainWindowTitle
   ```
2. **Stop and ask the user** (via whatever your environment's equivalent of
   `AskUserQuestion` is) to complete the sign-in/consent in that browser
   window. Never fabricate or assume the outcome — wait for a real
   response.
3. After they confirm, `sleep` 2-3 seconds and continue.

**Known environment quirk**: in this environment, the OS's default browser
has resolved to LibreWolf on at least one occasion, and LibreWolf could
not reach the loopback listener Dropbox's flow opens on
`localhost:52475` ("Unable to connect" in the browser). Edge and Chrome
have reliably worked for this same flow in this same environment. If a
sign-in fails with a browser-side connection error (not a Dropbox/Google
error), ask the user to copy the URL into Edge or Chrome instead, or to
temporarily switch their default browser, rather than assuming the OAuth
code is broken. This has not been root-caused (it's suspected to be a
session/sandbox boundary between where the browser runs and where the
`dotnet run` process's loopback listener actually lives) — don't burn time
re-diagnosing it, just route around it with a different browser.

**Silent reconnect**: once a provider has been signed in once, its
refresh token is persisted (`DropboxTokenStore` via Windows DPAPI at
`%AppData%\Sifra\dropbox-refresh-token.dat`; Google Drive via its own
`FileDataStore` at `%AppData%\Sifra\google-token-cache\`). A later app
launch calling `TrySilentSignIn()` (which `SyncScheduler`'s background
timer always uses, and which `EnsureSignedIn`/`EnsureActiveProviderSignedIn`
try before falling back to interactive) should reconnect with **no browser
popup at all**. If you want to verify this specifically: sign in once
interactively, close the app, relaunch, and confirm no browser opens while
sync still succeeds (check `operations.log`, below).

## Verifying results without going through the UI

These are faster and more reliable than trying to read state off the
screen, and work even while the vault is locked:

**The local database** (SQLite, plaintext-readable tables like
`settings`, encrypted-ciphertext tables like `credentials`/`credential_fields`):
```bash
python3 -c "
import sqlite3
conn = sqlite3.connect(r'C:\Users\<user>\AppData\Roaming\Sifra\vault.db')
cur = conn.cursor()
cur.execute('SELECT json_value FROM settings WHERE id = 1')
print(cur.fetchone())
"
```
(Swap in whatever table/query is relevant — `credentials`, `attachments`,
`vault_master_key_slots`, etc. See `src/Sifra.Vault/Storage/VaultDatabase.cs`
and each store's `EnsureSchema` for the current schema.)

**The audit log** (`%AppData%\Sifra\operations.log`, one JSON object per
line) — the most reliable way to confirm a background operation (sync,
auto-lock, credential add/delete) actually happened and succeeded, without
needing any UI feedback:
```powershell
Get-Content "$env:APPDATA\Sifra\operations.log" -Tail 10
```
Look for `"OperationName":"SyncNow"`/`"SyncBlobs"` with
`"Details":"outcome=success..."`, timestamps that make sense relative to
when you triggered the action, etc.

**Real cloud state** — when you need to confirm something actually landed
on (or was removed from) Dropbox/Google Drive, don't infer it from local
state alone. Write a small standalone console harness (see below) that
calls the real API directly and prints what it finds.

## Standalone harness pattern (bypassing the Desktop UI entirely)

For anything that's really a *library* question ("does this real API call
work at all", "is this data actually on the cloud", "does silent
reconnect really avoid a browser popup across a process restart") — don't
drive the whole Desktop UI. Write a throwaway console project that
references `Sifra.Vault` directly:

```
<scratchpad>/myharness/myharness.csproj:
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="C:\Users\firas\Documents\Sifra-AI-Project\src\Sifra.Vault\Sifra.Vault.csproj" />
  </ItemGroup>
</Project>
```

Then a `Program.cs` (top-level statements) that loads secrets, builds the
real service objects (e.g. `DropboxAuthProvider`, `RealDropboxEnvelopeApiClient`,
`VaultSyncService`), and calls them directly, printing results to the
console. This is how the Dropbox/Google Drive live-sync verification and
the post-crash cloud cleanup were done in this session. Always:
- Point any local vault data at a scratch directory
  (`Path.Combine(Path.GetTempPath(), "some-unique-name")`), never at the
  real `%AppData%\Sifra`, unless you specifically intend to affect the
  real app's data.
- Delete the scratch project directory (`rm -rf`) when done — don't leave
  throwaway projects lying around in the scratchpad.
- If it wrote real test data to a real cloud account, delete that too, in
  the same harness or a follow-up one (`Files.DeleteV2Async` for Dropbox,
  `driveService.Files.Delete(fileId).ExecuteAsync()` for Drive).

## Secrets needed for live provider testing

- `.secrets/dropbox.json` — `{ "appKey": "...", "appSecret": "..." }` (repo
  root, gitignored via `.secrets/` in `.gitignore`). Only `appKey` is
  actually used (PKCE public-client flow, no secret needed for the OAuth
  exchange itself).
- `.secrets/google-oauth.json` — `{ "clientId": "...", "clientSecret": "..." }`.
- Both are found by walking up from `AppContext.BaseDirectory` looking for
  `.secrets/<file>` (see `AppServices.FindSecretsFile` in
  `src/Sifra.Desktop/AppServices.cs`, and the equivalent in `src/Sifra.Cli/Program.cs`)
  — works from both the Desktop app's build output and a scratch harness
  placed anywhere under this repo checkout or its temp build dirs, since it
  walks up to 8 parent directories.
- If either file is missing, that provider is simply unavailable
  (`AppServices.DropboxAuth`/`GoogleDriveAuth` is `null`) — not an error,
  just nothing to test for that provider.

## Working with real vs. disposable test vault data

The Desktop app's real data lives at `%AppData%\Sifra` (i.e.
`C:\Users\<user>\AppData\Roaming\Sifra`) — there is exactly one such
directory; it's shared by every launch. **Before doing anything
destructive or exploratory to it** (fresh-vault testing, provider
migration testing, anything that touches Setup), back it up first:

```powershell
$dataDir = Join-Path $env:APPDATA "Sifra"
$backupDir = Join-Path $env:TEMP ("Sifra-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
if (Test-Path $dataDir) {
    Copy-Item -Path $dataDir -Destination $backupDir -Recurse
    Remove-Item -Path $dataDir -Recurse -Force
    Write-Output "Backed up to $backupDir and cleared for fresh start."
}
```
Restoring is the reverse: delete whatever's at `$dataDir` (after backing
that up too, if it's not just more disposable test data) and copy the
backup back in.

**Always ask the user before wiping `%AppData%\Sifra`** if there's any
chance it holds real, non-test data — this was explicitly confirmed
on a per-occasion basis throughout this session, not assumed.

## A worked example: full live sync + migration test

This is the actual sequence used to verify the Phase 3 cloud-sync work
end to end, as a template for similar future tests:

1. Build (`dotnet build src/Sifra.Desktop`), stop any running instance,
   back up + clear `%AppData%\Sifra`.
2. Launch (`dotnet run --project src/Sifra.Desktop --no-build`, backgrounded,
   logged to a file).
3. `Dump-Tree` to confirm you're on SetupView (fresh vault) or UnlockView
   (existing vault).
4. Drive Setup: select a sync provider radio, set a throwaway master
   password via `MasterPasswordBox`, click `CreateVaultButton`.
5. If a provider was selected, expect a real browser OAuth window —
   ask the user to approve it (Edge/Chrome preferred, see the LibreWolf
   note above).
6. Click "Continue to Unlock" (match by `Name`, no AutomationId as of this
   writing), re-enter the password, click `UnlockButton`.
7. Wait ~2s, then check `operations.log` for `SyncNow`/`SyncBlobs` with
   `outcome=success` — background sync runs automatically on unlock.
8. To test migration: click `Options` (main toolbar), find the new
   `Options` window via `EnumWindows`, click `MigrateSyncProviderButton`,
   find the new `Migrate Sync Provider` window the same way, select a
   destination provider radio, click `MigrateButton` — expect a second
   browser OAuth window for the new provider if not already signed in.
9. Verify: read the `MessageBox` result text, re-check `settings` table's
   `CloudSyncProvider` value in `vault.db`, and (for real confidence) use a
   standalone harness to confirm the envelope file actually exists on the
   new provider's real account.
10. Clean up: stop the app, restore the real `%AppData%\Sifra` from
    backup if this was meant to be a throwaway test, delete any test data
    written to real cloud accounts, delete any scratch harness projects.

## What NOT to do

- Don't guess at AutomationIds or screen layout — `Dump-Tree` first, every time.
- Don't try to fix screenshot capture — it's a known environment limitation, not a bug in your script.
- Don't type a real vault's master password into an automation script.
- Don't assume an OAuth approval happened — always wait for explicit user confirmation.
- Don't leave scratch harness projects or test cloud data behind — clean up every time.
- Don't skip the backup step before wiping `%AppData%\Sifra`.
- Don't redeclare the same `Add-Type` class name twice in one PowerShell invocation.
