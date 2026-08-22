(() => {
  const status = document.getElementById("bridge-status");
  const requestId = crypto.randomUUID();
  const routeNames = { configuration: "Configuration", brands: "Brands", books: "Books", process: "Process", outputs: "Outputs", diagnostics: "Diagnostics" };
  const content = document.getElementById("app-content");
  const brandSelect = document.getElementById("brand-select");
  const renderList = (title, items, empty) => `<div><p class="eyebrow">${title}</p><h1 class="mt-1 text-2xl font-semibold">${title}</h1></div><div class="panel mt-6"><ul>${items.length ? items.map((item) => `<li class="flex justify-between border-b border-slate-100 py-3"><span class="font-medium">${item.name ?? item.Name}</span><span class="text-emerald-700">Ready</span></li>`).join("") : `<li class="py-4 text-slate-500">${empty}</li>`}</ul></div>`;
  const setRoute = (route) => {
    document.querySelectorAll?.("[data-route]").forEach((button) => button.classList.toggle("nav-item-active", button.dataset.route === route));
    const subtitle = document.getElementById("page-subtitle");
    if (subtitle) subtitle.textContent = `${routeNames[route] ?? "Application"} workspace`;
    if (route === "brands") content.innerHTML = renderList("Brands", window.appSnapshot?.discovery?.brands ?? window.appSnapshot?.Discovery?.Brands ?? [], "No Brands found. Add a folder to brands/ then Refresh.");
    if (route === "books") content.innerHTML = renderList("Books", window.appSnapshot?.discovery?.books ?? window.appSnapshot?.Discovery?.Books ?? [], "No Books found. Add a Book folder to sources/ then Refresh.");
    if (route === "process") content.innerHTML = `<div><p class="eyebrow">Process</p><h1 class="mt-1 text-2xl font-semibold">Process session</h1></div><div class="panel mt-6">No active processing session. Prepare a queue from Books.</div>`;
    if (route === "outputs") content.innerHTML = `<div><p class="eyebrow">Outputs</p><h1 class="mt-1 text-2xl font-semibold">Published outputs</h1></div><div class="panel mt-6">No published runs discovered yet.</div>`;
    if (route === "diagnostics") content.innerHTML = `<div><p class="eyebrow">Diagnostics</p><h1 class="mt-1 text-2xl font-semibold">Application diagnostics</h1></div><div class="panel mt-6">Application root: ${window.appSnapshot?.discovery?.paths?.root?.value ?? window.appSnapshot?.Discovery?.Paths?.Root?.Value ?? "Loading…"}</div>`;
  };
  document.querySelectorAll?.("[data-route]").forEach((button) => button.addEventListener("click", () => setRoute(button.dataset.route)));

  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const ok = response.ok ?? response.Ok;
    const id = response.id ?? response.Id;
    const command = response.command ?? response.Command;
    const error = response.error ?? response.Error;
    if (ok && command === "app.snapshot") {
      window.appSnapshot = response.payload ?? response.Payload;
      const brands = window.appSnapshot.discovery?.brands ?? window.appSnapshot.Discovery?.Brands ?? [];
      if (brandSelect) brandSelect.innerHTML = brands.length ? brands.map((brand) => `<option>${brand.name ?? brand.Name}</option>`).join("") : "<option>No brands</option>";
      setRoute(document.querySelector?.(".nav-item-active")?.dataset.route ?? "configuration");
      status.textContent = "Connected";
    } else status.textContent = ok && id === requestId && command === "app.pong" ? "Connected" : `Bridge error: ${error ?? "unexpected response"}`;
  });

  window.chrome.webview.postMessage(JSON.stringify({
    version: 1,
    id: requestId,
    command: "app.ping"
  }));
  const refreshButton = document.getElementById("refresh-button");
  if (typeof refreshButton?.addEventListener === "function") refreshButton.addEventListener("click", () => window.chrome.webview.postMessage(JSON.stringify({ version: 1, id: crypto.randomUUID(), command: "app.refresh" })));
  window.chrome.webview.postMessage(JSON.stringify({ version: 1, id: crypto.randomUUID(), command: "app.refresh" }));
})();
