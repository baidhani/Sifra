// STORY-010: detects a login form on the page, asks the background script
// which credentials are offered for this URL, and — only after an explicit
// click on the in-page consent banner — requests a fill. No credential
// value ever reaches this script until the user has clicked "Fill".

(function () {
  const passwordInput = document.querySelector('input[type="password"]');
  if (!passwordInput) return; // no login form on this page — nothing to do

  const usernameInput = findUsernameInput(passwordInput);

  chrome.runtime.sendMessage({ type: "DISCOVER", url: location.href }, (response) => {
    if (!response || !response.ok) {
      if (response?.needsUnlock) showUnlockPrompt();
      return; // no match, needs-unlock, or a native host error — nothing to fill
    }
    if (response.credentials.length > 0) showConsentBanner(response.credentials);
  });

  function findUsernameInput(pwInput) {
    const form = pwInput.closest("form") || document;
    const candidates = form.querySelectorAll('input[type="text"], input[type="email"], input:not([type])');
    return candidates.length > 0 ? candidates[0] : null;
  }

  function showUnlockPrompt() {
    const banner = document.createElement("div");
    banner.style.cssText =
      "position:fixed;top:12px;right:12px;z-index:2147483647;background:#374151;color:#fff;" +
      "font:14px system-ui,sans-serif;padding:10px 14px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.3);";
    banner.textContent = "Sifra: unlock the extension (toolbar icon) to enable autofill on this page.";
    document.body.appendChild(banner);
    setTimeout(() => banner.remove(), 6000);
  }

  function showConsentBanner(credentials) {
    const banner = document.createElement("div");
    banner.setAttribute("id", "sifra-autofill-banner");
    banner.style.cssText =
      "position:fixed;top:12px;right:12px;z-index:2147483647;background:#1f2937;color:#fff;" +
      "font:14px system-ui,sans-serif;padding:12px 16px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.3);" +
      "display:flex;align-items:center;gap:10px;";

    const label = document.createElement("span");
    const cred = credentials[0];
    label.textContent = `Sifra: fill "${cred.username}" for ${cred.label}?`;

    const fillButton = document.createElement("button");
    fillButton.textContent = "Fill";
    fillButton.style.cssText = "background:#2563eb;color:#fff;border:none;border-radius:4px;padding:4px 10px;cursor:pointer;";
    fillButton.addEventListener("click", () => {
      // The only place in this extension that turns into a FILL request —
      // it only runs from a real user click on this banner.
      chrome.runtime.sendMessage(
        { type: "FILL", url: location.href, credentialId: cred.id },
        (fillResponse) => {
          if (fillResponse && fillResponse.ok) {
            if (usernameInput) usernameInput.value = fillResponse.username;
            passwordInput.value = fillResponse.password;
            usernameInput?.dispatchEvent(new Event("input", { bubbles: true }));
            passwordInput.dispatchEvent(new Event("input", { bubbles: true }));
          }
          banner.remove();
        }
      );
    });

    const dismissButton = document.createElement("button");
    dismissButton.textContent = "Dismiss";
    dismissButton.style.cssText = "background:transparent;color:#cbd5e1;border:none;cursor:pointer;";
    dismissButton.addEventListener("click", () => banner.remove());

    banner.append(label, fillButton, dismissButton);
    document.body.appendChild(banner);
  }
})();
