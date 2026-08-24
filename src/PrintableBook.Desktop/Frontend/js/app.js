(() => {
  const status = document.getElementById("bridge-status");
  const content = document.getElementById("app-content");
  const brandSelect = document.getElementById("brand-select");
  const routeNames = { configuration: "Configuration", brands: "Brands", books: "Books", process: "Process", outputs: "Outputs", diagnostics: "Diagnostics" };
  const state = { selectedBrand: "", selectedBookId: "", selectedBookIds: new Set(), selectedBookTab: "overview", bookFilter: "", bookStatus: "All", brandSettings: "{}" };

  const escapeHtml = (value) => String(value ?? "").replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" }[character]));
  const valueFor = (object, name, fallback = null) => object?.[name] ?? object?.[name[0].toUpperCase() + name.slice(1)] ?? fallback;
  const discovery = () => valueFor(window.appSnapshot, "discovery", {});
  const books = () => valueFor(discovery(), "books", []);
  const summaries = () => valueFor(window.appSnapshot, "bookSummaries", []);
  const bookId = (book) => valueFor(valueFor(book, "id", {}), "value", valueFor(book, "name", ""));
  const summaryFor = (book) => summaries().find((summary) => valueFor(valueFor(summary, "bookId", {}), "value", "") === bookId(book));
  const displayStatus = (value) => typeof value === "number" ? ["Not started", "Running", "Failed", "Cancelled", "Completed", "Interrupted"][value] ?? "Unknown" : value;
  const frameModeValue = (value) => {
    if (typeof value === "number") return ["auto", "enabled", "disabled"][value] ?? "auto";
    const normalized = String(value ?? "auto").toLowerCase();
    return ["auto", "enabled", "disabled"].includes(normalized) ? normalized : "auto";
  };
  const workspaceStatus = (summary) => displayStatus(valueFor(summary, "workspaceStatus", "Not started"));
  const statusClass = (value) => value === "Ready" || value === "Completed" || value === "Present" ? "status-good" : value === "Invalid" || value === "Failed" ? "status-bad" : value === "Needs selection" || value === "Running" ? "status-warn" : "status-muted";
  const badge = (value) => { const label = displayStatus(value); return `<span class="status-badge ${statusClass(label)}">${escapeHtml(label)}</span>`; };
  const send = (command, payload) => window.chrome.webview.postMessage(JSON.stringify({ version: 1, id: crypto.randomUUID(), command, ...(payload ? { payload } : {}) }));
  const dateTime = (value) => value ? new Date(value).toLocaleString() : "—";
  const panel = (title, body, extra = "") => `<section class="panel ${extra}"><h2 class="panel-title">${title}</h2>${body}</section>`;
  const selectedBook = () => books().find((book) => bookId(book) === state.selectedBookId);

  const renderConfiguration = () => {
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    const setting = (name, fallback) => valueFor(settings, name, fallback);
    content.innerHTML = `<div class="page-header"><div><h1>Configuration</h1><p>Manage global application settings.</p></div><div class="page-actions"><button class="button-secondary" data-action="refresh">Load</button><button class="button-primary" data-action="save-settings">Save</button></div></div><div class="detail-stack">${panel("Application", `<div class="form-grid two"><label class="field"><span>Maximum concurrency</span><input class="control" data-setting="maximumPageConcurrency" type="number" min="1" max="12" value="${setting("maximumPageConcurrency", 4)}"></label><label class="field"><span>Artwork dark threshold</span><input class="control" data-setting="artworkDetectionThreshold" type="number" min="0" max="255" value="${setting("artworkDetectionThreshold", 20)}"></label></div>`)}${panel("Interior processing", `<div class="form-grid three"><label class="field"><span>Max artwork side</span><input class="control" data-setting="artworkMaximumSide" type="number" min="1" value="${setting("artworkMaximumSide", 2270)}"></label><label class="field"><span>Working width</span><input class="control" data-setting="workingPageWidth" type="number" min="1" value="${setting("workingPageWidth", 2550)}"></label><label class="field"><span>Working height</span><input class="control" data-setting="workingPageHeight" type="number" min="1" value="${setting("workingPageHeight", 2550)}"></label><label class="field"><span>Final width</span><input class="control" data-setting="finalPageWidth" type="number" min="1" value="${setting("finalPageWidth", 2588)}"></label><label class="field"><span>Final height</span><input class="control" data-setting="finalPageHeight" type="number" min="1" value="${setting("finalPageHeight", 2625)}"></label><label class="field"><span>DPI</span><input class="control" data-setting="dpi" type="number" min="1" value="${setting("dpi", 300)}"></label></div>`)}${panel("PDF output", `<div class="form-grid two"><label class="field"><span>Interior physical width (inch)</span><input class="control" data-setting="interiorPdfWidthInches" type="number" min="0.1" step="0.1" value="${setting("interiorPdfWidthInches", 8.5)}"></label><label class="field"><span>Interior physical height (inch)</span><input class="control" data-setting="interiorPdfHeightInches" type="number" min="0.1" step="0.1" value="${setting("interiorPdfHeightInches", 8.5)}"></label></div>`)}</div>`;
  };

  const renderBrands = () => {
    const allBrands = valueFor(discovery(), "brands", []);
    if (!state.selectedBrand && allBrands.length) state.selectedBrand = valueFor(allBrands[0], "name", "");
    const selected = allBrands.find((brand) => valueFor(brand, "name", "") === state.selectedBrand);
    const assets = valueFor(selected, "assets", []);
    content.innerHTML = `<div class="page-header"><div><h1>Brands</h1><p>Inspect brand assets and isolated settings.</p></div></div><div class="master-detail"><section class="panel list-panel"><div class="list-title">Brands</div><ul class="item-list">${allBrands.length ? allBrands.map((brand) => `<li class="${valueFor(brand, "name", "") === state.selectedBrand ? "selected" : ""}" data-action="select-brand" data-brand-name="${escapeHtml(valueFor(brand, "name", ""))}"><span>${escapeHtml(valueFor(brand, "name", ""))}</span>${badge((valueFor(brand, "assets", []) ?? []).some((asset) => valueFor(asset, "status", "") === "Missing") ? "Attention" : "Ready")}</li>`).join("") : "<li class=\"empty-row\">No Brands found.</li>"}</ul></section><section class="detail-pane">${selected ? `${panel(escapeHtml(valueFor(selected, "name", "")), `<p class="detail-path">${escapeHtml(valueFor(valueFor(selected, "directory", {}), "value", ""))}</p><table class="data-table"><thead><tr><th>Asset</th><th>Type</th><th>Status</th></tr></thead><tbody>${assets.map((asset) => `<tr><td>${escapeHtml(valueFor(asset, "name", ""))}</td><td>${escapeHtml(valueFor(asset, "type", ""))}</td><td>${badge(valueFor(asset, "status", "Missing"))}</td></tr>`).join("")}</tbody></table>`) }${panel("Brand settings", `<textarea class="control settings-editor" data-brand-settings>${escapeHtml(state.brandSettings)}</textarea><div class="page-actions mt-3"><button class="button-secondary" data-action="load-brand-settings">Load</button><button class="button-primary" data-action="save-brand-settings">Save</button></div>`)} ` : panel("Brand detail", "<p class=\"empty-copy\">Select a Brand to inspect its assets.</p>")}</section></div>`;
  };

  const renderBookTabs = (book, summary) => {
    const checks = valueFor(summary, "validationChecks", []);
    const folders = valueFor(summary, "sourceFolders", []);
    const artifacts = valueFor(summary, "publishedArtifacts", []);
    const pages = valueFor(summary, "interiorPages", []);
    const sourcePages = valueFor(summary, "interiorSourcePages", []);
    const logs = valueFor(summary, "logs", []);
    const tabButton = (id, label) => `<button class="detail-tab ${state.selectedBookTab === id ? "active" : ""}" data-action="book-tab" data-book-tab="${id}">${label}</button>`;
    let body = "";
    if (state.selectedBookTab === "overview") {
      const frameModeRows = sourcePages.map((source) => {
        const sourceReference = valueFor(source, "sourceReference", "");
        const mode = frameModeValue(valueFor(source, "frameMode", "auto"));
        return `<tr><td class="detail-path">${escapeHtml(sourceReference)}</td><td><select class="control h-8" aria-label="Frame mode for ${escapeHtml(sourceReference)}" data-action="set-interior-frame-mode" data-book-id="${escapeHtml(bookId(book))}" data-source-reference="${escapeHtml(sourceReference)}"><option value="auto" ${mode === "auto" ? "selected" : ""}>Auto</option><option value="enabled" ${mode === "enabled" ? "selected" : ""}>Frame</option><option value="disabled" ${mode === "disabled" ? "selected" : ""}>No Frame</option></select></td></tr>`;
      }).join("");
      body = `<div class="summary-grid"><div><span>Status</span>${badge(workspaceStatus(summary))}</div><div><span>Validation</span>${badge(valueFor(summary, "validationStatus", "Checking"))}</div><div><span>Last run</span><strong>${dateTime(valueFor(summary, "lastRunAt", null))}</strong></div><div><span>Pages (interior)</span><strong>${valueFor(summary, "interiorSourcePageCount", 0)}</strong></div></div>${panel("Interior frame mode", sourcePages.length ? `<p class="mb-3 text-sm text-slate-500">Auto uses detected artwork type.</p><table class="data-table"><thead><tr><th>Interior source</th><th>Frame mode</th></tr></thead><tbody>${frameModeRows}</tbody></table>` : "<p class=\"empty-copy\">No discovered Interior images.</p>", "mt-4")}${panel("Folders", `<table class="data-table"><thead><tr><th>Folder</th><th>Status</th><th>Files</th></tr></thead><tbody>${folders.map((folder) => `<tr><td>${escapeHtml(valueFor(folder, "name", ""))}</td><td>${badge(valueFor(folder, "status", "Missing"))}</td><td>${valueFor(folder, "imageCount", 0)} image(s) / ${valueFor(folder, "fileCount", 0)} file(s)</td></tr>`).join("")}</tbody></table>`, "mt-4")}`;
    }
    if (state.selectedBookTab === "validation") body = panel("Validation checks", `<ul class="check-list">${checks.length ? checks.map((check) => { const warning = valueFor(check, "isWarning", false); const success = valueFor(check, "isSuccess", false); return `<li class="${warning ? "warning" : success ? "success" : "failure"}">${warning ? "!" : success ? "✓" : "✕"}<span>${escapeHtml(valueFor(check, "message", ""))}</span></li>`; }).join("") : "<li class=\"empty-row\">No validation result yet.</li>"}</ul>`);
    if (state.selectedBookTab === "processing") body = panel("Processing", `<dl class="summary-grid"><div><span>Workspace</span>${badge(workspaceStatus(summary))}</div><div><span>Current step</span><strong>${escapeHtml(valueFor(summary, "currentStep", "Not started"))}</strong></div><div><span>Completed pages</span><strong>${pages.length} / ${valueFor(summary, "interiorSourcePageCount", 0)}</strong></div></dl>${pages.length ? `<table class="data-table mt-4"><thead><tr><th>Page</th><th>Status</th><th>Final page</th></tr></thead><tbody>${pages.map((page) => `<tr><td>${escapeHtml(valueFor(page, "pageId", ""))}</td><td>${badge(valueFor(page, "status", ""))}</td><td class="detail-path">${escapeHtml(valueFor(page, "finalPagePath", ""))}</td></tr>`).join("")}</tbody></table>` : "<p class=\"empty-copy mt-4\">No processed interior pages yet.</p>"}`);
    if (state.selectedBookTab === "outputs") body = panel("Published outputs", artifacts.length ? `<ul class="artifact-list">${artifacts.map((artifact) => `<li>${escapeHtml(artifact)}</li>`).join("")}</ul>` : "<p class=\"empty-copy\">No published output yet.</p>");
    if (state.selectedBookTab === "logs") body = panel("Workspace logs", logs.length ? `<table class="data-table"><thead><tr><th>Time</th><th>Event</th><th>Detail</th></tr></thead><tbody>${logs.map((log) => `<tr><td>${dateTime(valueFor(log, "timestamp", null))}</td><td>${escapeHtml(valueFor(log, "eventName", ""))}</td><td>${escapeHtml(valueFor(log, "detail", ""))}</td></tr>`).join("")}</tbody></table>` : "<p class=\"empty-copy\">No workspace log entries yet.</p>");
    return `<div class="book-heading"><div><h2>${escapeHtml(valueFor(book, "name", ""))}</h2><p>${escapeHtml(valueFor(valueFor(book, "directory", {}), "value", ""))}</p></div><div class="page-actions"><button class="button-secondary" data-action="validate-book" data-book-id="${escapeHtml(bookId(book))}">Validate</button><button class="button-primary" data-action="queue-selected-book">Process Interior</button></div></div><nav class="detail-tabs">${tabButton("overview", "Overview")}${tabButton("validation", "Validation")}${tabButton("processing", "Processing")}${tabButton("outputs", "Outputs")}${tabButton("logs", "Logs")}</nav><div class="tab-body">${body}</div>`;
  };

  const renderBooks = () => {
    const allBooks = books();
    if (!state.selectedBookId && allBooks.length) state.selectedBookId = bookId(allBooks[0]);
    const statuses = ["All", "Ready", "Invalid", "Needs selection", "Running", "Interrupted", "Failed"];
    const statusCounts = statuses.map((name) => ({ name, count: name === "All" ? allBooks.length : allBooks.filter((book) => valueFor(summaryFor(book), "validationStatus", "") === name || workspaceStatus(summaryFor(book)) === name).length }));
    const filtered = allBooks.filter((book) => valueFor(book, "name", "").toLowerCase().includes(state.bookFilter.toLowerCase()) && (state.bookStatus === "All" || valueFor(summaryFor(book), "validationStatus", "") === state.bookStatus || workspaceStatus(summaryFor(book)) === state.bookStatus));
    const book = selectedBook();
    content.innerHTML = `<div class="page-header"><div><h1>Books</h1><p>Refresh and validate source book folders for Interior Processing.</p></div><div class="page-actions"><button class="button-secondary" data-action="refresh">Refresh</button><button class="button-secondary" data-action="validate-all">Validate all</button><button class="button-primary" data-action="go-process">Process Interior</button></div></div><div class="status-filters">${statusCounts.map(({ name, count }) => `<button class="${state.bookStatus === name ? "active" : ""}" data-action="book-status" data-book-status="${name}">${name}<strong>${count}</strong></button>`).join("")}</div><div class="master-detail books-layout"><section class="panel list-panel"><div class="list-title">Books (${allBooks.length})</div><input class="control w-full mt-3" data-action="filter-books" value="${escapeHtml(state.bookFilter)}" placeholder="Search book…"><ul class="item-list mt-3">${filtered.length ? filtered.map((item) => { const itemSummary = summaryFor(item); const id = bookId(item); return `<li class="book-row ${id === state.selectedBookId ? "selected" : ""}" data-action="select-book" data-book-id="${escapeHtml(id)}"><input type="checkbox" aria-label="Queue ${escapeHtml(valueFor(item, "name", ""))}" data-action="queue-book" data-book-id="${escapeHtml(id)}" ${state.selectedBookIds.has(id) ? "checked" : ""}><div><strong>${escapeHtml(valueFor(item, "name", ""))}</strong><small>${escapeHtml(workspaceStatus(itemSummary))}</small></div>${badge(valueFor(itemSummary, "validationStatus", "Checking"))}</li>`; }).join("") : "<li class=\"empty-row\">No Books match this view.</li>"}</ul></section><section class="detail-pane">${book ? renderBookTabs(book, summaryFor(book)) : panel("Book detail", "<p class=\"empty-copy\">Select a Book to inspect its state.</p>")}</section></div>`;
  };

  const renderProcess = (requestProcess = true) => {
    const session = window.processSnapshot;
    const active = valueFor(session, "isActive", false);
    const cancelling = valueFor(session, "isCancelling", false);
    const sessionQueue = valueFor(session, "queue", []);
    const hasSession = active || cancelling || sessionQueue.length > 0;
    const terminal = hasSession && !active && !cancelling;
    const pendingQueue = [...state.selectedBookIds].map((id) => {
      const book = books().find((candidate) => bookId(candidate) === id);
      return { bookId: { value: id }, status: "Ready", detail: valueFor(book, "name", id) };
    });
    const queue = hasSession ? sessionQueue : pendingQueue;
    const currentBook = valueFor(valueFor(session, "currentBookId", {}), "value", terminal ? "Last session" : "No active Book");
    const terminalStatuses = sessionQueue.map((entry) => displayStatus(valueFor(entry, "status", "Not started")));
    const derivedTerminalStep = terminalStatuses.length && terminalStatuses.every((status) => status === "Completed")
      ? "Completed"
      : terminalStatuses.length && terminalStatuses.every((status) => status === "Failed")
        ? "Failed"
        : terminalStatuses.length && terminalStatuses.every((status) => status === "Cancelled")
          ? "Cancelled"
          : "Completed";
    const currentStep = valueFor(session, "currentStep", null) || (terminal ? derivedTerminalStep : "Waiting");
    const completed = valueFor(session, "pagesCompleted", 0);
    const total = valueFor(session, "pagesTotal", 0);
    const percent = total ? Math.min(100, Math.round((completed / total) * 100)) : 0;
    content.innerHTML = `<div class="page-header"><div><h1>Process Interior</h1><p>${cancelling ? "Stopping Interior Processing session…" : active ? "Active Interior Processing session" : terminal ? "Last Interior Processing session" : "Prepare a selected interior-only book queue."}</p></div>${active ? cancelling ? '<button class="button-danger" disabled>Stopping processing…</button>' : '<button class="button-danger" data-action="cancel-process">Cancel session</button>' : ""}</div><div class="process-grid">${panel(hasSession ? (terminal ? "Last session" : "Queue") : "Selected queue", `<ul class="queue-list">${queue.length ? queue.map((entry) => `<li><span>${escapeHtml(valueFor(valueFor(entry, "bookId", {}), "value", ""))}</span>${badge(valueFor(entry, "status", "NotStarted"))}<small>${escapeHtml(valueFor(entry, "detail", "Waiting"))}</small></li>`).join("") : "<li class=\"empty-row\">Select Books on the Books page.</li>"}</ul>`)}${panel("Current step", `<div class="process-book"><strong>${escapeHtml(currentBook)}</strong><span>${escapeHtml(currentStep)}</span></div><div class="progress-track"><span style="width:${percent}%"></span></div><p class="progress-copy">${completed} / ${total || "?"} pages · ${valueFor(session, "workerLimit", 0) || "?"} workers</p><div class="page-actions mt-4">${active || cancelling ? "" : `<button class="button-primary" data-action="start-process" ${state.selectedBookIds.size ? "" : "disabled"}>${terminal ? "Start New Interior Processing" : "Start Interior Processing"}</button>`}</div>`)}</div>`;
    if (requestProcess) send("process.get");
  };

  const renderOutputs = () => {
    const outputSummaries = summaries().filter((summary) => valueFor(summary, "publishedArtifacts", []).length);
    content.innerHTML = `<div class="page-header"><div><h1>Outputs</h1><p>Generated, validated Interior PDF artifacts.</p></div></div>${outputSummaries.length ? `<div class="output-grid">${outputSummaries.flatMap((summary) => valueFor(summary, "publishedArtifacts", []).map((artifact) => `<article class="output-card"><div class="pdf-mark">PDF</div><div><h2>${escapeHtml(artifact.split(/[\\/]/).pop())}</h2><p>${escapeHtml(valueFor(valueFor(summary, "bookId", {}), "value", ""))}</p><small>${dateTime(valueFor(summary, "lastRunAt", null))}</small></div></article>`)).join("")}</div>` : panel("Latest run", "<p class=\"empty-copy\">No published outputs discovered.</p>")}${panel("Previous runs", `<table class="data-table"><thead><tr><th>Book</th><th>Status</th><th>Interior artifacts</th></tr></thead><tbody>${outputSummaries.length ? outputSummaries.map((summary) => `<tr><td>${escapeHtml(valueFor(valueFor(summary, "bookId", {}), "value", ""))}</td><td>${badge(workspaceStatus(summary))}</td><td>${valueFor(summary, "publishedArtifacts", []).length}</td></tr>`).join("") : "<tr><td colspan=\"3\" class=\"empty-row\">No history yet.</td></tr>"}</tbody></table>`, "mt-5")}`;
  };

  const renderDiagnostics = () => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const folders = valueFor(summary, "sourceFolders", []);
    const logs = valueFor(summary, "logs", []);
    content.innerHTML = `<div class="page-header"><div><h1>Diagnostics</h1><p>Inspect the current workspace without giving the web page direct file access.</p></div><select class="control diagnostic-select" data-action="diagnostic-book">${books().map((item) => `<option value="${escapeHtml(bookId(item))}" ${bookId(item) === state.selectedBookId ? "selected" : ""}>${escapeHtml(valueFor(item, "name", ""))}</option>`).join("")}</select></div><div class="diagnostics-grid">${panel("Workspace info", book ? `<dl class="path-grid"><div><dt>Workspace state</dt><dd>${badge(workspaceStatus(summary))}</dd></div><div><dt>Current step</dt><dd>${escapeHtml(valueFor(summary, "currentStep", "Not started"))}</dd></div><div><dt>Last run</dt><dd>${dateTime(valueFor(summary, "lastRunAt", null))}</dd></div></dl>` : "<p class=\"empty-copy\">No Book selected.</p>")}${panel("Files", `<table class="data-table"><thead><tr><th>Folder</th><th>Status</th><th>Images</th></tr></thead><tbody>${folders.map((folder) => `<tr><td>${escapeHtml(valueFor(folder, "name", ""))}</td><td>${badge(valueFor(folder, "status", "Missing"))}</td><td>${valueFor(folder, "imageCount", 0)}</td></tr>`).join("")}</tbody></table>`)}</div>${panel("Latest log", logs.length ? `<ul class="log-list">${logs.slice(-12).reverse().map((log) => `<li><time>${dateTime(valueFor(log, "timestamp", null))}</time><span>${escapeHtml(valueFor(log, "eventName", ""))} · ${escapeHtml(valueFor(log, "detail", ""))}</span></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No logs for this Book.</p>", "mt-5")}`;
  };

  const render = (route, requestProcess = true) => {
    document.querySelectorAll("[data-route]").forEach((button) => button.classList.toggle("nav-item-active", button.dataset.route === route));
    const subtitle = document.getElementById("page-subtitle");
    if (subtitle) subtitle.textContent = `${routeNames[route] ?? "Application"} workspace`;
    if (route === "configuration") renderConfiguration();
    if (route === "brands") renderBrands();
    if (route === "books") renderBooks();
    if (route === "process") renderProcess(requestProcess);
    if (route === "outputs") renderOutputs();
    if (route === "diagnostics") renderDiagnostics();
  };

  document.querySelectorAll("[data-route]").forEach((button) => button.addEventListener("click", () => render(button.dataset.route)));
  content.addEventListener("click", (event) => {
    const target = event.target.closest("[data-action]");
    if (!target) return;
    const action = target.dataset.action;
    if (action === "refresh" || action === "validate-all") send("app.refresh");
    if (action === "save-settings") { const payload = {}; document.querySelectorAll("[data-setting]").forEach((input) => { payload[input.dataset.setting] = Number(input.value); }); send("settings.save", payload); }
    if (action === "select-brand") { state.selectedBrand = target.dataset.brandName; if (brandSelect) brandSelect.value = state.selectedBrand; render("brands"); }
    if (action === "load-brand-settings") send("brand.settings.get", { brandName: state.selectedBrand });
    if (action === "save-brand-settings") send("brand.settings.save", { brandName: state.selectedBrand, json: document.querySelector("[data-brand-settings]")?.value ?? "{}" });
    if (action === "select-book") { state.selectedBookId = target.dataset.bookId; state.selectedBookTab = "overview"; render("books"); }
    if (action === "queue-book") { const id = target.dataset.bookId; if (target.checked) state.selectedBookIds.add(id); else state.selectedBookIds.delete(id); }
    if (action === "queue-selected-book") { if (state.selectedBookId) state.selectedBookIds.add(state.selectedBookId); render("process"); }
    if (action === "book-tab") { state.selectedBookTab = target.dataset.bookTab; render("books", false); }
    if (action === "book-status") { state.bookStatus = target.dataset.bookStatus; render("books", false); }
    if (action === "validate-book") send("book.validate", { bookId: target.dataset.bookId });
    if (action === "select-cover") send("book.cover.select", { bookId: target.dataset.bookId, coverReference: target.dataset.coverReference });
    if (action === "go-process") render("process");
    if (action === "start-process") send("process.start", { bookIds: [...state.selectedBookIds], brandName: state.selectedBrand || brandSelect?.value || null, mode: "interior-only" });
    if (action === "cancel-process") send("process.cancel");
  });
  content.addEventListener("input", (event) => { if (event.target.dataset.action === "filter-books") { state.bookFilter = event.target.value; render("books", false); } });
  content.addEventListener("change", (event) => {
    if (event.target.dataset.action === "diagnostic-book") { state.selectedBookId = event.target.value; render("diagnostics", false); }
    if (event.target.dataset.action === "set-interior-frame-mode") send("book.interior.frame-mode.set", { bookId: event.target.dataset.bookId, sourceReference: event.target.dataset.sourceReference, mode: event.target.value });
  });

  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const ok = valueFor(response, "ok", false);
    const command = valueFor(response, "command", "");
    if (ok && command === "app.pong") {
      status.textContent = "Connected";
    } else if (ok && command === "app.snapshot") {
      window.appSnapshot = valueFor(response, "payload", {});
      const allBrands = valueFor(discovery(), "brands", []);
      if (!state.selectedBrand && allBrands.length) state.selectedBrand = valueFor(allBrands[0], "name", "");
      if (brandSelect) brandSelect.innerHTML = allBrands.length ? allBrands.map((brand) => `<option>${escapeHtml(valueFor(brand, "name", ""))}</option>`).join("") : "<option>No brands</option>";
      if (brandSelect) brandSelect.value = state.selectedBrand;
      render(document.querySelector(".nav-item-active")?.dataset.route ?? "configuration", false);
      status.textContent = "Connected";
    } else if (ok && command === "settings.saved") {
      window.appSnapshot = { ...(window.appSnapshot ?? {}), globalSettings: valueFor(response, "payload", {}) };
      render("configuration", false);
      status.textContent = "Settings saved";
    } else if (ok && command === "process.snapshot") {
      window.processSnapshot = valueFor(response, "payload", {});
      if (document.querySelector(".nav-item-active")?.dataset.route === "process") render("process", false);
      status.textContent = "Connected";
    } else if (ok && (command === "brand.settings" || command === "brand.settings.saved")) {
      state.brandSettings = valueFor(response, "payload", "{}");
      if (document.querySelector(".nav-item-active")?.dataset.route === "brands") render("brands", false);
      status.textContent = command === "brand.settings.saved" ? "Brand settings saved" : "Connected";
    } else status.textContent = `Bridge error: ${valueFor(response, "error", "unexpected response")}`;
  });

  const refreshButton = document.getElementById("refresh-button");
  if (refreshButton) refreshButton.addEventListener("click", () => send("app.refresh"));
  if (brandSelect) brandSelect.addEventListener("change", () => { state.selectedBrand = brandSelect.value; });
  window.setInterval(() => { if (valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false)) send("process.get"); }, 1000);
  send("app.ping");
  send("app.refresh");
})();
