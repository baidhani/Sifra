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

  // Capture-on-submit, prompt-on-next-page-load (same pattern Chrome/
  // Bitwarden use): grab whatever was typed the moment the form is
  // submitted — before navigation actually happens and this script's DOM
  // access to that page is gone — and hand it to background.js to hold
  // until the resulting page has loaded. Capture phase (the `true` below)
  // so this still fires even if the page's own JS calls
  // stopPropagation() on the submit event.
  document.addEventListener(
    "submit",
    (e) => {
      const form = e.target;
      if (!(form instanceof HTMLFormElement)) return;

      const submittedPasswordInput = form.querySelector('input[type="password"]');
      if (!submittedPasswordInput || !submittedPasswordInput.value) return;

      const submittedUsernameInput = findUsernameInput(submittedPasswordInput);
      const username = submittedUsernameInput ? submittedUsernameInput.value : "";
      if (!username) return; // nothing meaningful to offer to save without a username

      chrome.runtime.sendMessage({
        type: "CAPTURE_LOGIN",
        url: location.href,
        username,
        password: submittedPasswordInput.value,
      });
    },
    true
  );

  // Runs on every page load, in every tab — a no-op unless a login form was
  // just submitted in this same tab a moment ago (see the submit listener
  // above and background.js's pending-capture storage). This is why it's
  // unconditional rather than gated behind `if (passwordInput)`: the page
  // you land on *after* logging in (a dashboard, say) almost never has a
  // password field itself.
  chrome.runtime.sendMessage({ type: "CHECK_PENDING_CAPTURE" }, (response) => {
    if (response && response.ok) {
      showSaveOrUpdateBanner(response);
    }
  });

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
      "position:fixed;top:12px;right:12px;z-index:2147483647;background:#1e1e1e;color:#fff;" +
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

  function makeField(labelText, value, type) {
    const wrapper = document.createElement("label");
    wrapper.style.cssText = "display:flex;flex-direction:column;gap:3px;font-size:12px;color:#9ca3af;";

    const input = document.createElement("input");
    input.type = type;
    input.value = value;
    input.style.cssText =
      "background:#141414;color:#fff;border:1px solid #3a3a3a;border-radius:6px;padding:6px 8px;font:14px system-ui,sans-serif;";

    wrapper.append(labelText, input);
    return { wrapper, input };
  }

  const EYE_ICON =
    '<svg width="15" height="15" viewBox="0 0 20 20" fill="none"><path d="M2 10s3-5.5 8-5.5S18 10 18 10s-3 5.5-8 5.5S2 10 2 10Z" stroke="currentColor" stroke-width="1.5"/><circle cx="10" cy="10" r="2.3" stroke="currentColor" stroke-width="1.5"/></svg>';
  const EYE_OFF_ICON =
    '<svg width="15" height="15" viewBox="0 0 20 20" fill="none"><path d="M2 10s3-5.5 8-5.5S18 10 18 10s-3 5.5-8 5.5S2 10 2 10Z" stroke="currentColor" stroke-width="1.5"/><circle cx="10" cy="10" r="2.3" stroke="currentColor" stroke-width="1.5"/><path d="M3 3l14 14" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>';

  // Masked by default — this field previously showed the plaintext password
  // in the open on the page (a shoulder-surfing/screen-recording exposure),
  // with no way to hide it back once shown.
  function makePasswordField(labelText, value) {
    const { wrapper, input } = makeField(labelText, value, "password");
    input.style.paddingRight = "30px";

    const inputRow = document.createElement("div");
    inputRow.style.cssText = "position:relative;display:flex;";

    const toggle = document.createElement("button");
    toggle.type = "button";
    toggle.innerHTML = EYE_ICON;
    toggle.title = "Show password";
    toggle.setAttribute("aria-label", "Show password");
    toggle.style.cssText =
      "position:absolute;right:4px;top:50%;transform:translateY(-50%);background:transparent;border:none;" +
      "color:#9ca3af;cursor:pointer;padding:4px;display:flex;align-items:center;justify-content:center;width:auto;";
    toggle.addEventListener("click", () => {
      const revealed = input.type === "text";
      input.type = revealed ? "password" : "text";
      toggle.innerHTML = revealed ? EYE_ICON : EYE_OFF_ICON;
      toggle.title = revealed ? "Show password" : "Hide password";
    });

    // Re-parent input under inputRow so the toggle can sit inside it
    // without disturbing the label text already appended by makeField.
    wrapper.removeChild(input);
    inputRow.appendChild(input);
    inputRow.appendChild(toggle);
    wrapper.appendChild(inputRow);

    return { wrapper, input };
  }

  // Save-new / update-changed password prompt — shown once per captured
  // form submission (see the submit listener and CHECK_PENDING_CAPTURE
  // above). Editable fields rather than a plain "Save?" confirmation, so a
  // wrong guess at the label/username (e.g. this content script grabbed the
  // wrong field as "username") can be corrected before it's written to the
  // vault, instead of having to go fix it in Sifra Desktop afterward.
  function showSaveOrUpdateBanner(capture) {
    const isNew = capture.status === "New";
    const host = (() => {
      try {
        return new URL(capture.url).hostname;
      } catch {
        return capture.url;
      }
    })();

    const banner = document.createElement("div");
    banner.id = "sifra-save-banner";
    banner.style.cssText =
      "position:fixed;top:12px;right:12px;z-index:2147483647;background:#1e1e1e;color:#fff;" +
      "font:14px system-ui,sans-serif;border-radius:10px;box-shadow:0 4px 16px rgba(0,0,0,.35);" +
      "padding:14px;display:flex;flex-direction:column;gap:10px;width:260px;";

    const header = document.createElement("div");
    header.style.cssText = "display:flex;align-items:center;gap:8px;font-weight:600;";
    const logo = document.createElement("img");
    logo.src = LOGO_URL;
    logo.alt = "Sifra";
    logo.style.cssText = "width:18px;height:18px;flex-shrink:0;border-radius:4px;";
    const headerText = document.createElement("span");
    headerText.textContent = isNew ? "Save this password?" : "Update saved password?";
    header.append(logo, headerText);
    banner.appendChild(header);

    let labelField, usernameField, passwordField;
    if (isNew) {
      labelField = makeField("Label", host, "text");
      usernameField = makeField("Username", capture.username, "text");
      passwordField = makePasswordField("Password", capture.password);
      banner.append(labelField.wrapper, usernameField.wrapper, passwordField.wrapper);
    } else {
      const info = document.createElement("div");
      info.style.cssText = "font-size:13px;color:#d1d5db;";
      info.textContent = `"${capture.existingLabel}" — new password:`;
      passwordField = makePasswordField("Password", capture.password);
      banner.append(info, passwordField.wrapper);
    }

    const buttons = document.createElement("div");
    buttons.style.cssText = "display:flex;gap:8px;margin-top:2px;";

    const primaryButton = document.createElement("button");
    primaryButton.textContent = isNew ? "Save" : "Update";
    primaryButton.style.cssText =
      "flex:1;background:#2563eb;color:#fff;border:none;border-radius:999px;padding:9px;cursor:pointer;font:inherit;font-weight:500;";
    primaryButton.addEventListener("click", () => {
      primaryButton.disabled = true;
      primaryButton.textContent = "Saving...";

      const message = isNew
        ? {
            type: "SAVE_CAPTURED_LOGIN",
            label: labelField.input.value,
            url: capture.url,
            username: usernameField.input.value,
            password: passwordField.input.value,
          }
        : {
            type: "UPDATE_CAPTURED_LOGIN",
            credentialId: capture.credentialId,
            password: passwordField.input.value,
          };

      chrome.runtime.sendMessage(message, (response) => {
        banner.remove();
        showToast(response && response.ok ? "Sifra: saved." : "Sifra: couldn't save — try again from the vault.");
      });
    });

    const dismissButton = document.createElement("button");
    dismissButton.textContent = "Not now";
    dismissButton.style.cssText =
      "background:transparent;color:#cbd5e1;border:none;border-radius:999px;cursor:pointer;font:inherit;padding:9px;";
    dismissButton.addEventListener("click", () => banner.remove());

    buttons.append(primaryButton, dismissButton);
    banner.appendChild(buttons);
    document.body.appendChild(banner);
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
