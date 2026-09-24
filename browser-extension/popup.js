const unpairedView = document.getElementById("unpairedView");
const lockedView = document.getElementById("lockedView");
const unlockedView = document.getElementById("unlockedView");
const statusEl = document.getElementById("status");
const pairButton = document.getElementById("pairButton");

// Small inline SVGs (not emoji, for consistent rendering across platforms)
// so a status message reads at a glance instead of requiring the text to be
// parsed — success/error/info previously all looked identical (plain gray
// text), which made a real error easy to miss on a small popup.
const ICONS = {
  success:
    '<svg class="icon" width="14" height="14" viewBox="0 0 20 20" fill="none"><circle cx="10" cy="10" r="10" fill="#15803d"/><path d="M6 10.5l2.5 2.5L14 7.5" stroke="#fff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  error:
    '<svg class="icon" width="14" height="14" viewBox="0 0 20 20" fill="none"><circle cx="10" cy="10" r="10" fill="#b91c1c"/><path d="M10 6v5" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/><circle cx="10" cy="13.5" r="1" fill="#fff"/></svg>',
  info:
    '<svg class="icon" width="14" height="14" viewBox="0 0 20 20" fill="none"><circle cx="10" cy="10" r="10" fill="#6b7280"/><path d="M10 9v5" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/><circle cx="10" cy="6.5" r="1" fill="#fff"/></svg>',
};

function setStatus(text, kind = "info") {
  statusEl.className = kind;
  statusEl.innerHTML = text ? `${ICONS[kind]}<span>${text}</span>` : "";
}

const matchesEmpty = document.getElementById("matchesEmpty");
const matchesList = document.getElementById("matchesList");

function render(paired, unlocked) {
  unpairedView.hidden = paired;
  lockedView.hidden = !paired || unlocked;
  unlockedView.hidden = !paired || !unlocked;
  if (paired && unlocked) loadMatches();
}

// Re-runs DISCOVER for the active tab so the popup can offer the matching
// login(s) directly — previously the unlocked view only ever offered "Lock
// now", even when the current page had one or more saved credentials
// (visible only via the toolbar badge, with no way to act on it from here).
function loadMatches() {
  matchesList.hidden = true;
  matchesList.innerHTML = "";
  matchesEmpty.hidden = false;
  matchesEmpty.textContent = "Checking this page for saved logins...";

  chrome.tabs.query({ active: true, currentWindow: true }, ([tab]) => {
    if (!tab || !tab.url || !tab.id) {
      matchesEmpty.textContent = "Visit a saved login page to see autofill.";
      return;
    }

    chrome.runtime.sendMessage({ type: "DISCOVER", url: tab.url }, (response) => {
      if (!response || !response.ok || response.credentials.length === 0) {
        matchesEmpty.textContent = "No saved logins match this page.";
        return;
      }
      renderMatches(response.credentials, tab.id, tab.url);
    });
  });
}

// No auto-clear timer here: an extension popup's whole JS context is
// destroyed the moment it loses focus (e.g. the instant you click into
// another window to paste), so a setTimeout scheduled here would almost
// never actually fire — and the background service worker can't pick up
// the slack either, since navigator.clipboard is a document/window API
// unavailable to a service worker. Auto-clear would need to live in the
// content script of whatever tab is open instead, which is a bigger,
// separate feature (and unreliable across arbitrary sites' own clipboard
// permissions policy) — not attempted here.
function copyToClipboard(text) {
  navigator.clipboard.writeText(text);
}

// Distinct icons per field (rather than one generic copy icon repeated
// twice) so which button copies what is visible at a glance, not just via
// the tooltip.
const COPY_USERNAME_ICON =
  '<svg width="17" height="17" viewBox="0 0 20 20" fill="none"><circle cx="10" cy="7" r="3" stroke="currentColor" stroke-width="1.9"/><path d="M4 16c0-3 2.7-5 6-5s6 2 6 5" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"/></svg>';
const COPY_PASSWORD_ICON =
  '<svg width="17" height="17" viewBox="0 0 20 20" fill="none"><circle cx="7" cy="10" r="3" stroke="currentColor" stroke-width="1.9"/><path d="M9.8 10h7.2M13.5 10v3M16 10v2" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"/></svg>';

function makeCopyButton(title, icon, onClick) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "copyButton";
  button.title = title;
  button.setAttribute("aria-label", title);
  button.innerHTML = icon;
  button.addEventListener("click", (e) => {
    e.stopPropagation(); // don't also trigger the row's fill click
    onClick();
  });
  return button;
}

function renderMatches(credentials, tabId, tabUrl) {
  const sorted = [...credentials].sort((a, b) => Boolean(b.username) - Boolean(a.username));
  matchesEmpty.hidden = true;
  matchesList.hidden = false;

  for (const cred of sorted) {
    const row = document.createElement("div");
    row.className = "matchRow";

    const main = document.createElement("button");
    main.type = "button";
    main.className = "matchMain";

    const label = document.createElement("span");
    label.className = "matchLabel";
    label.textContent = cred.label;

    const username = document.createElement("span");
    username.className = "matchUsername" + (cred.username ? "" : " empty");
    username.textContent = cred.username || "No username saved";

    main.append(label, username);
    main.addEventListener("click", () => {
      chrome.tabs.sendMessage(tabId, { type: "FILL_FROM_POPUP", credential: cred }, () => {
        // "Could not establish connection. Receiving end does not exist" —
        // content.js isn't running in this tab (most commonly: the tab was
        // already open when the extension was installed/reloaded, which
        // invalidates any content script it already injected until the
        // page is refreshed). Silently doing nothing here previously made
        // a fill click look like it just didn't work.
        if (chrome.runtime.lastError) {
          setStatus("Couldn't reach this page — refresh it and try again.", "error");
          return;
        }
        window.close();
      });
    });

    const copyUsernameButton = makeCopyButton("Copy username", COPY_USERNAME_ICON, () => {
      if (!cred.username) return;
      copyToClipboard(cred.username);
      setStatus("Username copied.", "success");
    });

    const copyPasswordButton = makeCopyButton("Copy password", COPY_PASSWORD_ICON, () => {
      // The popup never holds a decrypted password itself — DISCOVER only
      // ever returns id/label/username (see native host's "discover"
      // handler) — so this asks for one via the same FILL action fillCredential
      // in content.js would use, just consumed here instead of written into
      // the page.
      chrome.runtime.sendMessage({ type: "FILL", url: tabUrl, credentialId: cred.id }, (response) => {
        if (!response || !response.ok) {
          setStatus("Couldn't copy password.", "error");
          return;
        }
        copyToClipboard(response.password);
        setStatus("Password copied.", "success");
      });
    });

    row.append(main, copyUsernameButton, copyPasswordButton);
    matchesList.appendChild(row);
  }
}

chrome.runtime.sendMessage({ type: "STATUS" }, (response) => {
  render(Boolean(response && response.paired), Boolean(response && response.unlocked));
});

pairButton.addEventListener("click", () => {
  pairButton.disabled = true;
  setStatus("Waiting for approval in Sifra Desktop...", "info");

  chrome.runtime.sendMessage({ type: "PAIR" }, (response) => {
    pairButton.disabled = false;
    if (response && response.ok) {
      setStatus("Paired. Enter your master password to unlock.", "success");
      render(true, false);
    } else if (response && response.error === "DesktopNotRunning") {
      setStatus("Open Sifra Desktop, then try again.", "error");
    } else if (response && response.error === "PairingDenied") {
      setStatus("Pairing was declined in Sifra Desktop.", "error");
    } else if (response && response.error === "PairingTimedOut") {
      setStatus("No response from Sifra Desktop — try again.", "error");
    } else {
      setStatus("Could not pair with Sifra Desktop.", "error");
    }
  });
});

const passwordInput = document.getElementById("password");

function attemptUnlock() {
  const password = passwordInput.value;
  if (!password) return;

  chrome.runtime.sendMessage({ type: "UNLOCK", password }, (response) => {
    passwordInput.value = "";
    if (response && response.ok) {
      setStatus("Unlocked. Visit a saved login page to see autofill.", "success");
      render(true, true);
    } else if (response && response.needsPairing) {
      setStatus("This browser is no longer paired — pair again.", "error");
      render(false, false);
    } else {
      setStatus("Wrong master password.", "error");
    }
  });
}

document.getElementById("unlockButton").addEventListener("click", attemptUnlock);
// The password field isn't in a <form>, so Enter did nothing by default —
// only a direct click on "Unlock for this session" worked.
passwordInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") attemptUnlock();
});

document.getElementById("lockButton").addEventListener("click", () => {
  chrome.runtime.sendMessage({ type: "LOCK" }, () => {
    setStatus("Locked.", "info");
    render(true, false);
  });
});
