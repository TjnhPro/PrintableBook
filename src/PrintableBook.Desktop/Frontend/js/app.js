(() => {
  const status = document.getElementById("bridge-status");
  const requestId = crypto.randomUUID();
  const routeNames = { configuration: "Configuration", brands: "Brands", books: "Books", process: "Process", outputs: "Outputs", diagnostics: "Diagnostics" };
  const setRoute = (route) => {
    document.querySelectorAll?.("[data-route]").forEach((button) => button.classList.toggle("nav-item-active", button.dataset.route === route));
    const subtitle = document.getElementById("page-subtitle");
    if (subtitle) subtitle.textContent = `${routeNames[route] ?? "Application"} workspace`;
  };
  document.querySelectorAll?.("[data-route]").forEach((button) => button.addEventListener("click", () => setRoute(button.dataset.route)));

  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const ok = response.ok ?? response.Ok;
    const id = response.id ?? response.Id;
    const command = response.command ?? response.Command;
    const error = response.error ?? response.Error;
    status.textContent = ok && id === requestId && command === "app.pong" ? "Connected" : `Bridge error: ${error ?? "unexpected response"}`;
  });

  window.chrome.webview.postMessage(JSON.stringify({
    version: 1,
    id: requestId,
    command: "app.ping"
  }));
})();
