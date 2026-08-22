(() => {
  const status = document.getElementById("bridge-status");
  const requestId = crypto.randomUUID();

  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    status.textContent = response.ok && response.id === requestId && response.command === "app.pong"
      ? "Desktop bridge connected."
      : `Desktop bridge error: ${response.error ?? "unexpected_response"}`;
  });

  window.chrome.webview.postMessage(JSON.stringify({
    version: 1,
    id: requestId,
    command: "app.ping"
  }));
})();
