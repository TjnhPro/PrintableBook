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
    if (route === "brands") { const brands = window.appSnapshot?.discovery?.brands ?? window.appSnapshot?.Discovery?.Brands ?? []; content.innerHTML = `<div class="grid grid-cols-[360px_1fr] gap-5"><section>${renderList("Brands", brands, "No Brands found. Add a folder to brands/ then Refresh.")}</section><section class="panel mt-[74px]"><h2 class="panel-title">Brand detail</h2><p class="mt-2 text-sm text-slate-500">Overview · Assets · Settings</p><dl class="mt-5 grid gap-3 text-sm"><div><dt class="font-medium">Assets</dt><dd class="text-slate-500">Status is discovered from the selected Brand folder.</dd></div><div><dt class="font-medium">Brand settings</dt><dd class="text-slate-500">brand.json is separate from Global Settings.</dd></div></dl></section></div>`; }
    if (route === "books") { const books = window.appSnapshot?.discovery?.books ?? window.appSnapshot?.Discovery?.Books ?? []; content.innerHTML = `<div><p class="eyebrow">Books</p><h1 class="mt-1 text-2xl font-semibold">Books</h1></div><div class="mt-6 grid grid-cols-[360px_1fr] gap-5"><section class="panel"><input class="control w-full" placeholder="Search books"><ul class="mt-3">${books.length ? books.map((book) => `<li class="flex items-center gap-2 border-b border-slate-100 py-3"><input type="checkbox" data-book-id="${book.id?.value ?? book.Id?.Value ?? book.name}"><span>${book.name ?? book.Name}</span><span class="ml-auto text-xs text-emerald-700">Ready</span></li>`).join("") : "<li class=\"py-4 text-slate-500\">No Books found.</li>"}</ul></section><section class="panel"><h2 class="panel-title">Book detail</h2><p class="mt-2 text-sm text-slate-500">Overview · Structure · Processing · Interior · Output · Logs</p><div class="mt-5 flex gap-2"><button class="button-secondary">Validate</button><button class="button-secondary">Add selected to Process</button></div></section></div>`; }
    if (route === "books") content.innerHTML += `<section class="panel mt-5"><h2 class="panel-title">Book overview and structure</h2><p class="mt-2 text-sm text-slate-500">Selected Book shows source folders, workspace state and non-destructive actions.</p></section><section class="panel mt-5"><h2 class="panel-title">Validation</h2><p class="mt-2 text-sm text-slate-500">Validation is explicit and separate from Process. Results remain visible here.</p></section>`;
    if (route === "books") content.innerHTML += `<section class="panel mt-5"><h2 class="panel-title">Processing history</h2><p class="mt-2 text-sm text-slate-500">Scan · Validation · Interior Processing · Shuffle · Assembly · PDF Export · Publish</p></section>`;
    if (route === "books") content.innerHTML += `<section class="panel mt-5"><h2 class="panel-title">Interior page detail</h2><p class="mt-2 text-sm text-slate-500">Source · Trim · Resize · Frame · Working Page · Final Page with raster/DPI inspection.</p></section>`;
    if (route === "process") content.innerHTML = `<div><p class="eyebrow">Process</p><h1 class="mt-1 text-2xl font-semibold">Process session</h1></div><div class="panel mt-6"><h2 class="panel-title">Selected queue</h2><p class="mt-2 text-sm text-slate-500">Books use one selected Brand and execute sequentially.</p><button class="button-secondary mt-4">Start processing</button></div>`;
    if (route === "process") content.innerHTML += `<section class="panel mt-5"><h2 class="panel-title">Live session monitor</h2><p class="mt-2 text-sm text-slate-500">Current Book · current step · page progress · bounded workers · cancellation.</p></section>`;
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
