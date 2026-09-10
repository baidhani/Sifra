// STORY-010 walking skeleton. Talks to the Sifra native messaging host,
// which wraps the already-tested CredentialAutofillService (C#). This
// file only relays messages and holds the unlocked vault password for
// the browser session.
//
// The password lives in chrome.storage.session, not a module-level
// variable: MV3 service workers are unloaded after ~30s idle and lose
// all in-memory state, but chrome.storage.session survives a service
// worker restart while still being memory-only (never written to disk)
// and cleared when the browser closes — the same "session-held
// plaintext credential" trade-off already flagged for VaultSession
// (STORY-004) and GoogleDriveSyncProvider (STORY-007), just persisted
// correctly for this runtime's lifecycle model.

const NATIVE_HOST_NAME = "com.sifra.nativehost";
const SESSION_KEY = "vaultPassword";

let nativePort = null;
// Sifra.NativeHost processes one full request/response cycle per loop
// iteration (see Program.cs) — no concurrency on its side — so responses
// arrive strictly in the order requests were sent. A FIFO queue is enough
// to route each response back to its caller without needing the host to
// echo a request id.
const pendingRequests = [];

function getVaultPassword() {
  return chrome.storage.session.get(SESSION_KEY).then((result) => result[SESSION_KEY] ?? null);
}

function getNativePort() {
  if (!nativePort) {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
    nativePort.onMessage.addListener((response) => {
      const resolve = pendingRequests.shift();
      if (resolve) resolve(response);
    });
    nativePort.onDisconnect.addListener(() => {
      nativePort = null;
      const error = chrome.runtime.lastError?.message ?? "Native host disconnected.";
      while (pendingRequests.length > 0) {
        pendingRequests.shift()({ ok: false, error: "NativeHostDisconnected", message: error });
      }
    });
  }
  return nativePort;
}

function sendNativeRequest(message) {
  return new Promise((resolve) => {
    pendingRequests.push(resolve);
    getNativePort().postMessage(message);
  });
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.type === "STATUS") {
    getVaultPassword().then((password) => sendResponse({ ok: true, unlocked: password !== null }));
    return true;
  }

  if (message.type === "UNLOCK") {
    chrome.storage.session.set({ [SESSION_KEY]: message.password }).then(() => sendResponse({ ok: true }));
    return true;
  }

  if (message.type === "LOCK") {
    chrome.storage.session.remove(SESSION_KEY).then(() => sendResponse({ ok: true }));
    return true;
  }

  if (message.type === "DISCOVER") {
    getVaultPassword().then((vaultPassword) => {
      if (!vaultPassword) {
        sendResponse({ ok: false, needsUnlock: true });
        return;
      }
      sendNativeRequest({ action: "discover", vaultCredential: vaultPassword, url: message.url }).then(sendResponse);
    });
    return true; // keep the message channel open for the async response
  }

  if (message.type === "FILL") {
    getVaultPassword().then((vaultPassword) => {
      if (!vaultPassword) {
        sendResponse({ ok: false, needsUnlock: true });
        return;
      }
      sendNativeRequest({
        action: "fill",
        vaultCredential: vaultPassword,
        url: message.url,
        credentialId: message.credentialId,
        consent: true, // only ever sent in response to an explicit user click — see content.js
      }).then(sendResponse);
    });
    return true;
  }

  return false;
});
