// STORY-010: detects a login form on the page, asks the background script
// which credentials are offered for this URL, and lets the popup (the one
// picker UI — see popup.js) request a fill. No credential value ever
// reaches this script until that explicit action happens.
//
// This used to also render its own in-page picker banner when more than one
// credential matched, styled independently (dark theme) from the popup's
// own picker (light theme) — the same choice shown twice, differently. The
// popup is kept as the single picker; this script now only surfaces passive
// "you need to unlock/pair" nudges, which the popup can't say on its own
// without first being opened.

(function () {
  // The FILL_FROM_POPUP listener below used to only get registered after
  // finding a password field at document_idle — on an SPA login page (Steam
  // confirmed) the form is rendered by client-side JS after document_idle,
  // so the field didn't exist yet, the script returned early, and the
  // listener never registered at all. No amount of refreshing helped
  // because the same race happens on every load. Registering it
  // unconditionally, and re-querying the DOM fresh at fill time (by which
  // point the user has had time to see and click into the rendered form),
  // fixes this regardless of whether the form was present at page-load.
  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message.type === "FILL_FROM_POPUP") {
      fillCredential(message.credential);
      sendResponse({ ok: true });
    }
  });

  const passwordInput = document.querySelector('input[type="password"]');
  if (passwordInput) {
    chrome.runtime.sendMessage({ type: "DISCOVER", url: location.href }, (response) => {
      if (!response || !response.ok) {
        if (response?.needsUnlock) showUnlockPrompt();
        else if (response?.needsPairing) showPairingPrompt();
      }
      // Matches (response.ok && credentials.length > 0) are surfaced via the
      // toolbar badge (see background.js) and picked/filled from the popup —
      // no in-page banner for that case any more.
    });
  }

  function findUsernameInput(pwInput) {
    const form = pwInput.closest("form") || document;
    const candidates = form.querySelectorAll('input[type="text"], input[type="email"], input:not([type])');
    return candidates.length > 0 ? candidates[0] : null;
  }

  // "Identifier-first" login flows (Outlook, Google, and most large
  // providers now) split the form across two pages: one asking only for the
  // email/username, then — after submitting — a second page asking only for
  // the password. There's no password field to anchor a search on for the
  // first page, so this searches the whole document instead, skipping
  // hidden inputs (a common decoy/honeypot pattern) rather than always
  // grabbing the first match regardless of visibility.
  function findStandaloneUsernameInput() {
    const candidates = document.querySelectorAll('input[type="email"], input[type="text"], input:not([type])');
    for (const input of candidates) {
      if (input.offsetParent !== null) return input;
    }
    return null;
  }

  function showUnlockPrompt() {
    showToast("Sifra: unlock the extension (toolbar icon) to enable autofill on this page.");
  }

  function showPairingPrompt() {
    showToast("Sifra: pair this browser with Sifra Desktop (toolbar icon) to enable autofill.");
  }

  const LOGO_URL = chrome.runtime.getURL("icons/symbol128.png");

  function showToast(text) {
    const banner = document.createElement("div");
    banner.style.cssText =
      "position:fixed;top:12px;right:12px;z-index:2147483647;background:#1f2937;color:#fff;" +
      "font:14px system-ui,sans-serif;border-radius:10px;box-shadow:0 4px 16px rgba(0,0,0,.35);" +
      "padding:10px 14px;display:flex;align-items:center;gap:10px;min-width:220px;max-width:320px;";

    const logo = document.createElement("img");
    logo.src = LOGO_URL;
    logo.alt = "Sifra";
    logo.style.cssText = "width:18px;height:18px;flex-shrink:0;border-radius:4px;";

    const label = document.createElement("span");
    label.textContent = text;

    banner.append(logo, label);
    document.body.appendChild(banner);
    setTimeout(() => banner.remove(), 6000);
  }

  // React (and similar frameworks) wrap <input> in a controlled component:
  // it replaces the element's own "value" property with one that only the
  // framework's synthetic event system updates, so a plain `input.value =
  // x` silently gets overwritten back to the framework's own state on the
  // next render — the DOM briefly shows the fill, then it's gone/unfilled.
  // Calling the real, un-overridden setter from HTMLInputElement's own
  // prototype bypasses that, and is the standard workaround for this
  // (confirmed on Steam's login page, which is React-based).
  function setNativeInputValue(input, value) {
    const nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set;
    nativeSetter.call(input, value);
  }

  function fillCredential(cred) {
    // Always query fresh, at fill time — never trust a reference captured
    // when this script first ran (see the listener-registration comment
    // above for why: on an SPA the form may not have existed yet then).
    const currentPasswordInput = document.querySelector('input[type="password"]');

    if (!currentPasswordInput) {
      // Identifier-first page (see findStandaloneUsernameInput) — nothing to
      // put a password into here, so just fill the username using what
      // DISCOVER already gave the popup, rather than making a full FILL
      // request (which would also hand back the password, pointlessly,
      // since there's nowhere on this page for it to go).
      const identifierInput = findStandaloneUsernameInput();
      if (!identifierInput || !cred.username) return;
      setNativeInputValue(identifierInput, cred.username);
      identifierInput.dispatchEvent(new Event("input", { bubbles: true }));
      identifierInput.dispatchEvent(new Event("change", { bubbles: true }));
      return;
    }

    const currentUsernameInput = findUsernameInput(currentPasswordInput);

    // The only place in this extension that turns into a FILL request — it
    // only runs from a real user click on the popup's match list.
    chrome.runtime.sendMessage(
      { type: "FILL", url: location.href, credentialId: cred.id },
      (fillResponse) => {
        if (fillResponse && fillResponse.ok) {
          if (currentUsernameInput) {
            setNativeInputValue(currentUsernameInput, fillResponse.username);
            currentUsernameInput.dispatchEvent(new Event("input", { bubbles: true }));
            currentUsernameInput.dispatchEvent(new Event("change", { bubbles: true }));
          }
          setNativeInputValue(currentPasswordInput, fillResponse.password);
          currentPasswordInput.dispatchEvent(new Event("input", { bubbles: true }));
          currentPasswordInput.dispatchEvent(new Event("change", { bubbles: true }));
        }
      }
    );
  }
})();
