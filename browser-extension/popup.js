const unpairedView = document.getElementById("unpairedView");
const lockedView = document.getElementById("lockedView");
const unlockedView = document.getElementById("unlockedView");
const statusEl = document.getElementById("status");
const pairButton = document.getElementById("pairButton");

function render(paired, unlocked) {
  unpairedView.hidden = paired;
  lockedView.hidden = !paired || unlocked;
  unlockedView.hidden = !paired || !unlocked;
}

chrome.runtime.sendMessage({ type: "STATUS" }, (response) => {
  render(Boolean(response && response.paired), Boolean(response && response.unlocked));
});

pairButton.addEventListener("click", () => {
  pairButton.disabled = true;
  statusEl.textContent = "Waiting for approval in Sifra Desktop...";

  chrome.runtime.sendMessage({ type: "PAIR" }, (response) => {
    pairButton.disabled = false;
    if (response && response.ok) {
      statusEl.textContent = "Paired. Enter your master password to unlock.";
      render(true, false);
    } else if (response && response.error === "DesktopNotRunning") {
      statusEl.textContent = "Open Sifra Desktop, then try again.";
    } else if (response && response.error === "PairingDenied") {
      statusEl.textContent = "Pairing was declined in Sifra Desktop.";
    } else if (response && response.error === "PairingTimedOut") {
      statusEl.textContent = "No response from Sifra Desktop — try again.";
    } else {
      statusEl.textContent = "Could not pair with Sifra Desktop.";
    }
  });
});

document.getElementById("unlockButton").addEventListener("click", () => {
  const password = document.getElementById("password").value;
  if (!password) return;

  chrome.runtime.sendMessage({ type: "UNLOCK", password }, (response) => {
    document.getElementById("password").value = "";
    if (response && response.ok) {
      statusEl.textContent = "Unlocked. Visit a saved login page to see autofill.";
      render(true, true);
    } else if (response && response.needsPairing) {
      statusEl.textContent = "This browser is no longer paired — pair again.";
      render(false, false);
    } else {
      statusEl.textContent = "Wrong master password.";
    }
  });
});

document.getElementById("lockButton").addEventListener("click", () => {
  chrome.runtime.sendMessage({ type: "LOCK" }, () => {
    statusEl.textContent = "Locked.";
    render(true, false);
  });
});
