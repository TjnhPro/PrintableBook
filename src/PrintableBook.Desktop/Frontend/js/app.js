(() => {
  const status = document.getElementById("bridge-status");
  const requestId = crypto.randomUUID();

  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const normalizedResponse = {
      ok: response.ok ?? response.Ok,
      id: response.id ?? response.Id,
      command: response.command ?? response.Command,
      error: response.error ?? response.Error
    };

    status.textContent = normalizedResponse.ok && normalizedResponse.id === requestId && normalizedResponse.command === "app.pong"
      ? "Desktop bridge connected."
      : `Desktop bridge error: ${normalizedResponse.error ?? "unexpected_response"}`;
  });

  window.chrome.webview.postMessage(JSON.stringify({
    version: 1,
    id: requestId,
    command: "app.ping"
  }));
})();
