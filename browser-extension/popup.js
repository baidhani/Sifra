const lockedView = document.getElementById("lockedView");
const unlockedView = document.getElementById("unlockedView");
const statusEl = document.getElementById("status");

function render(unlocked) {
  lockedView.hidden = unlocked;
  unlockedView.hidden = !unlocked;
}

chrome.runtime.sendMessage({ type: "STATUS" }, (response) => {
  render(Boolean(response && response.unlocked));
});

document.getElementById("unlockButton").addEventListener("click", () => {
  const password = document.getElementById("password").value;
  if (!password) return;

  chrome.runtime.sendMessage({ type: "UNLOCK", password }, (response) => {
    document.getElementById("password").value = "";
    if (response && response.ok) {
      statusEl.textContent = "Unlocked. Visit a saved login page to see autofill.";
      render(true);
    } else {
      statusEl.textContent = "Could not unlock.";
    }
  });
});

document.getElementById("lockButton").addEventListener("click", () => {
  chrome.runtime.sendMessage({ type: "LOCK" }, () => {
    statusEl.textContent = "Locked.";
    render(false);
  });
});
