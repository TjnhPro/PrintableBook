(() => {
  const status = document.getElementById("bridge-status");
  const content = document.getElementById("app-content");
  const brandSelect = document.getElementById("brand-select");
  const routeNames = { configuration: "Settings", brands: "Brands & templates", books: "Book Library", process: "Interior processing", outputs: "PDF Library", diagnostics: "Diagnostics" };
  const state = { selectedBrand: "", selectedBookId: "", selectedBookIds: new Set(), selectedBookTab: "overview", bookDrawerOpen: false, drawerFocusTitle: false, restoreBookFocus: false, bookDrawerScrollTop: 0, bookInteriorDrafts: new Map(), introTemplateDimensions: new Map(), bookInteriorSavePending: false, bookFilter: "", bookStatus: "All", bookPage: 1, bookView: "grid", bookSort: "activity", brandSettings: "{}", selectedAssetReference: "", assetView: "grid", assetFilter: "", assetFolder: "All folders", assetSearchFocused: false, assetSearchCaret: 0, pdfLibrarySearch: "", pdfLibrarySort: "newest", pdfLibraryPage: 1, pdfLibraryView: "grid", pdfLibrarySearchFocused: false, pdfLibrarySearchCaret: 0, applicationLoadState: "idle", applicationLoadError: "", libraryRefreshTaskId: "", libraryRefreshPollTimer: null, libraryRefreshResultRequested: false, cacheCleanupTaskId: "", cacheCleanupPollTimer: null, cacheCleanupResultRequested: false, cacheCleanupActive: false, processStartPending: false, lastTerminalRefreshSession: "", diagnosticsTab: "summary", backgroundTasks: [], pendingCommands: new Map() };

  const escapeHtml = (value) => String(value ?? "").replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" }[character]));
  const valueFor = (object, name, fallback = null) => object?.[name] ?? object?.[name[0].toUpperCase() + name.slice(1)] ?? fallback;
  const discovery = () => valueFor(window.appSnapshot, "discovery", {});
  const books = () => valueFor(discovery(), "books", []);
  const brands = () => valueFor(discovery(), "brands", []);
  const activeBrand = () => brands().find((brand) => valueFor(brand, "name", "") === (state.selectedBrand || brandSelect?.value || "")) ?? null;
  const summaries = () => valueFor(window.appSnapshot, "bookSummaries", []);
  const bookId = (book) => valueFor(valueFor(book, "id", {}), "value", valueFor(book, "name", ""));
  const summaryFor = (book) => summaries().find((summary) => valueFor(valueFor(summary, "bookId", {}), "value", "") === bookId(book));
  const pdfLibraryBookName = (book, summary) => bookId(book) || valueFor(valueFor(summary, "bookId", {}), "value", "");
  const pdfLibraryPageSize = 12;
  const pdfLibraryOutputSize = (summary) => valueFor(summary, "outputSummaries", []).reduce((total, output) => total + (Number(valueFor(output, "fileSizeBytes", 0)) || 0), 0);
  const pdfLibraryGeneratedAt = (summary) => {
    const outputTimes = valueFor(summary, "outputSummaries", []).map((output) => new Date(valueFor(output, "generatedAt", 0)).getTime()).filter((value) => Number.isFinite(value) && value > 0);
    if (outputTimes.length) return Math.max(...outputTimes);
    const lastRun = new Date(valueFor(summary, "lastRunAt", 0)).getTime();
    return Number.isFinite(lastRun) ? lastRun : 0;
  };
  const eligiblePdfLibraryBooks = () => books().map((book) => ({ book, summary: summaryFor(book) })).filter(({ summary }) => summary && workspaceStatus(summary) === "Completed" && valueFor(summary, "outputSummaries", []).length > 0);
  const pdfLibraryBooks = () => {
    const items = eligiblePdfLibraryBooks();
    const search = state.pdfLibrarySearch.trim().toLocaleLowerCase();
    const filtered = search ? items.filter(({ book, summary }) => pdfLibraryBookName(book, summary).toLocaleLowerCase().includes(search)) : items;
    return [...filtered].sort((left, right) => {
      if (state.pdfLibrarySort === "name") return pdfLibraryBookName(left.book, left.summary).localeCompare(pdfLibraryBookName(right.book, right.summary), undefined, { sensitivity: "base" });
      if (state.pdfLibrarySort === "size") return pdfLibraryOutputSize(right.summary) - pdfLibraryOutputSize(left.summary);
      return pdfLibraryGeneratedAt(right.summary) - pdfLibraryGeneratedAt(left.summary);
    });
  };
  const displayStatus = (value) => typeof value === "number" ? ["Not started", "Running", "Failed", "Cancelled", "Completed", "Interrupted"][value] ?? "Unknown" : value;
  const frameModeValue = (value) => {
    if (typeof value === "number") return ["auto", "enabled", "disabled"][value] ?? "auto";
    const normalized = String(value ?? "auto").toLowerCase();
    return ["auto", "enabled", "disabled"].includes(normalized) ? normalized : "auto";
  };
  const workspaceStatus = (summary) => displayStatus(valueFor(summary, "workspaceStatus", "Not started"));
  const productionStatus = (summary, book = null) => {
    const workspace = workspaceStatus(summary);
    const validation = valueFor(summary, "validationStatus", "Needs review");
    const outputs = valueFor(summary, "outputSummaries", []);
    if (workspace === "Failed" || validation === "Invalid") return "Failed";
    if (workspace === "Running") return "Processing";
    if (book && !introReadiness(book, summary).ready) return "Needs review";
    if (outputs.some((output) => ["Verified", "Available"].includes(valueFor(output, "verificationStatus", "")))) return "PDF ready";
    return validation === "Ready" ? "Ready" : "Needs review";
  };
  const bookFrameState = (summary) => {
    const modes = valueFor(summary, "interiorSourcePages", []).map((source) => frameModeValue(valueFor(source, "frameMode", "auto")));
    if (!modes.length) return "Needs review";
    if (modes.includes("enabled")) return "Frame";
    if (modes.includes("disabled")) return "No frame";
    return "Auto";
  };
  const statusClass = (value) => value === "Ready" || value === "Completed" || value === "Present" ? "status-good" : value === "Invalid" || value === "Failed" ? "status-bad" : value === "Needs selection" || value === "Running" ? "status-warn" : "status-muted";
  const badge = (value) => { const label = displayStatus(value); return `<span class="status-badge ${statusClass(label)}">${escapeHtml(label)}</span>`; };
  const send = (command, payload) => {
    const id = crypto.randomUUID();
    state.pendingCommands.set(id, command);
    window.chrome.webview.postMessage(JSON.stringify({ version: 1, id, command, ...(payload ? { payload } : {}) }));
    return id;
  };
  const dateTime = (value) => value ? new Date(value).toLocaleString() : "—";
  const elapsedTime = (value) => {
    if (!value) return "—";
    const totalSeconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${String(seconds).padStart(2, "0")}`;
  };
  const fileSize = (bytes) => {
    const value = Number(bytes) || 0;
    if (value >= 1024 ** 3) return `${(value / (1024 ** 3)).toFixed(1)} GB`;
    if (value >= 1024 ** 2) return `${(value / (1024 ** 2)).toFixed(1)} MB`;
    if (value >= 1024) return `${Math.round(value / 1024)} KB`;
    return `${value} B`;
  };
  const panel = (title, body, extra = "") => `<section class="panel ${extra}"><h2 class="panel-title">${title}</h2>${body}</section>`;
  const currentRoute = () => document.querySelector(".nav-item-active")?.dataset.route ?? "books";
  const applicationIsLoading = () => state.applicationLoadState === "loading" || state.applicationLoadState === "refreshing";
  const processIsActive = () => valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false);
  const cacheCleanupBlocked = () => applicationIsLoading() || processIsActive() || state.cacheCleanupActive;
  const updateGlobalRefreshControl = () => {
    const refreshButton = document.getElementById("refresh-button");
    if (!refreshButton) return;
    const loading = applicationIsLoading();
    refreshButton.disabled = loading;
    refreshButton.setAttribute("aria-busy", String(loading));
    refreshButton.textContent = state.applicationLoadState === "refreshing" ? "Refreshing…" : state.applicationLoadState === "loading" ? "Loading…" : "Refresh";
  };
  const refreshAction = (label = "Refresh") => `<button class="button-secondary" data-action="refresh" ${applicationIsLoading() ? "disabled" : ""}>${state.applicationLoadState === "refreshing" ? "Refreshing…" : label}</button>`;
  const renderLoadFailure = () => `<section class="panel" role="alert"><h2 class="panel-title">Unable to load library</h2><p class="panel-note">${escapeHtml(state.applicationLoadError || "Application refresh failed.")}</p><div class="page-actions mt-4"><button class="button-primary" data-action="refresh">Retry</button></div></section>`;
  const renderRefreshFailure = () => `<section class="refresh-failure" role="alert"><span>Refresh failed</span><span>${escapeHtml(state.applicationLoadError || "Application refresh failed.")}</span><button class="button-secondary" data-action="refresh">Retry</button></section>`;
  const beginApplicationRefresh = () => {
    if (applicationIsLoading()) return;
    state.applicationLoadState = window.appSnapshot ? "refreshing" : "loading";
    state.applicationLoadError = "";
    state.libraryRefreshTaskId = "";
    state.libraryRefreshResultRequested = false;
    render(currentRoute(), false);
    send("app.refresh");
  };
  const pollLibraryRefresh = () => {
    if (state.libraryRefreshTaskId) send("task.get", { taskId: state.libraryRefreshTaskId });
  };
  const observeLibraryRefresh = (task) => {
    const taskId = valueFor(task, "taskId", "");
    if (!taskId) return;
    state.libraryRefreshTaskId = taskId;
    const taskState = valueFor(task, "state", "Queued");
    if (["Queued", "Running", "Cancelling"].includes(taskState)) {
      if (state.libraryRefreshPollTimer === null) state.libraryRefreshPollTimer = window.setInterval(pollLibraryRefresh, 250);
      return;
    }
    if (state.libraryRefreshPollTimer !== null && window.clearInterval) window.clearInterval(state.libraryRefreshPollTimer);
    state.libraryRefreshPollTimer = null;
    if (taskState === "Completed" && !state.libraryRefreshResultRequested) {
      state.libraryRefreshResultRequested = true;
      send("app.refresh.result", { taskId });
      return;
    }
    state.applicationLoadState = "failed";
    state.applicationLoadError = valueFor(task, "errorMessage", "Application refresh failed.");
    render(currentRoute(), false);
  };
  const pollCacheCleanup = () => {
    if (state.cacheCleanupTaskId) send("task.get", { taskId: state.cacheCleanupTaskId });
  };
  const observeCacheCleanup = (task) => {
    const taskId = valueFor(task, "taskId", "");
    if (!taskId) return;
    state.cacheCleanupTaskId = taskId;
    const taskState = valueFor(task, "state", "Queued");
    if (["Queued", "Running", "Cancelling"].includes(taskState)) {
      state.cacheCleanupActive = true;
      if (state.cacheCleanupPollTimer === null) state.cacheCleanupPollTimer = window.setInterval(pollCacheCleanup, 250);
      if (currentRoute() === "books") render("books", false);
      return;
    }
    if (state.cacheCleanupPollTimer !== null && window.clearInterval) window.clearInterval(state.cacheCleanupPollTimer);
    state.cacheCleanupPollTimer = null;
    state.cacheCleanupActive = false;
    if (taskState === "Completed" && !state.cacheCleanupResultRequested) {
      state.cacheCleanupResultRequested = true;
      send("cache.clear.result", { taskId });
      return;
    }
    state.cacheCleanupTaskId = "";
    state.cacheCleanupResultRequested = false;
    status.textContent = taskState === "Cancelled" ? "Cache cleanup cancelled" : valueFor(task, "errorMessage", "Cache cleanup failed.");
    if (currentRoute() === "books") render("books", false);
  };
  const selectedBook = () => books().find((book) => bookId(book) === state.selectedBookId);
  const assetsFor = (summary) => valueFor(summary, "assets", []);
  const assetForReference = (summary, sourceReference) => assetsFor(summary).find((asset) => valueFor(asset, "sourceReference", "") === sourceReference);
  const interiorDraftFor = (id, create = false) => {
    let draft = state.bookInteriorDrafts.get(id);
    if (!draft && create) { draft = { assets: new Map() }; state.bookInteriorDrafts.set(id, draft); }
    return draft ?? null;
  };
  const clearInteriorDraft = (id) => state.bookInteriorDrafts.delete(id);
  const hasInteriorDraft = (id) => {
    const draft = interiorDraftFor(id);
    return Boolean(draft && (draft.hasBackground !== undefined || draft.hasIntro !== undefined || draft.introSourceReferences !== undefined || draft.assets.size));
  };
  const effectiveBackground = (book, summary) => {
    const draft = interiorDraftFor(bookId(book));
    return draft?.hasBackground ?? valueFor(summary, "hasBackground", false);
  };
  const effectiveInteriorAsset = (book, asset) => {
    const change = interiorDraftFor(bookId(book))?.assets.get(valueFor(asset, "sourceReference", ""));
    return {
      isActive: change?.active ?? valueFor(asset, "isActive", true),
      frameMode: change?.frameMode ?? frameModeValue(valueFor(asset, "frameMode", "auto"))
    };
  };
  const trimEmptyInteriorDraft = (id, draft) => { if (draft.hasBackground === undefined && draft.hasIntro === undefined && draft.introSourceReferences === undefined && draft.assets.size === 0) clearInteriorDraft(id); };
  const stageBackgroundChange = (book, summary, enabled) => {
    const id = bookId(book);
    const draft = interiorDraftFor(id, true);
    if (enabled === valueFor(summary, "hasBackground", false)) delete draft.hasBackground;
    else draft.hasBackground = enabled;
    trimEmptyInteriorDraft(id, draft);
  };
  const stageInteriorAssetChange = (book, asset, field, value) => {
    const id = bookId(book);
    const reference = valueFor(asset, "sourceReference", "");
    const draft = interiorDraftFor(id, true);
    const change = draft.assets.get(reference) ?? {};
    const original = field === "active" ? valueFor(asset, "isActive", true) : frameModeValue(valueFor(asset, "frameMode", "auto"));
    if (value === original) delete change[field]; else change[field] = value;
    if (change.active === undefined && change.frameMode === undefined) draft.assets.delete(reference); else draft.assets.set(reference, change);
    trimEmptyInteriorDraft(id, draft);
  };
  const persistedIntroSourceReferences = (summary) => {
    const sources = [
      ...(valueFor(summary, "interiorSourcePages", []) ?? []).map((page) => ({ sourceKey: valueFor(page, "sourceKey", ""), sourceReference: valueFor(page, "sourceReference", "") })),
      ...assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior").map((asset) => ({ sourceKey: valueFor(asset, "relativePath", ""), sourceReference: valueFor(asset, "sourceReference", "") }))
    ];
    return (valueFor(summary, "selectedIntroInteriorSourceKeys", []) ?? []).map((sourceKey) =>
      sources.find((source) => String(source.sourceKey).toLowerCase() === String(sourceKey).toLowerCase())?.sourceReference ?? sourceKey);
  };
  const effectiveIntro = (book, summary) => {
    const draft = interiorDraftFor(bookId(book));
    return {
      hasIntro: draft?.hasIntro ?? valueFor(summary, "hasIntro", false),
      sourceReferences: draft?.introSourceReferences ?? persistedIntroSourceReferences(summary)
    };
  };
  const introTemplateAssetId = (asset) => encodeURIComponent(`${valueFor(activeBrand(), "name", "")}\u0000${valueFor(asset, "key", "")}`);
  const isSupportedIntroTemplateSize = (width, height) => (width === 1024 && height === 1024) || (width === 2048 && height === 2048);
  const introReadiness = (book, summary) => {
    const selection = effectiveIntro(book, summary);
    const customCandidates = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    if (selection.hasIntro) {
      if (!selection.sourceReferences.length) return { ready: false, reason: "Custom Intro mode needs at least one selected Book interior page." };
      const sources = new Set(customCandidates.map((asset) => String(valueFor(asset, "sourceReference", "")).toLowerCase()));
      if (selection.sourceReferences.some((reference) => !sources.has(String(reference).toLowerCase()))) return { ready: false, reason: "A selected custom Intro page is missing from Book interior." };
      return { ready: true, reason: "" };
    }
    const brand = activeBrand();
    if (!brand) return { ready: false, reason: "Choose a Brand before processing Intro templates." };
    const templates = (valueFor(brand, "introTemplateAssets", []) ?? []).filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", "")));
    if (!templates.length) return { ready: false, reason: "The current Brand has no eligible Intro templates." };
    const effectiveTemplates = templates;
    if (effectiveTemplates.some((asset) => state.introTemplateDimensions.get(introTemplateAssetId(asset))?.valid === false)) return { ready: false, reason: "An effective Intro template is unreadable or must be 1024 × 1024 or 2048 × 2048 pixels." };
    return { ready: true, reason: "" };
  };
  const processingReadiness = (book, summary) => {
    if (hasInteriorDraft(bookId(book))) return { ready: false, reason: "Save Interior changes before processing." };
    if (valueFor(summary, "validationStatus", "Needs review") !== "Ready") return { ready: false, reason: "Run Interior preflight until this Book is ready." };
    return introReadiness(book, summary);
  };
  const sameKeys = (left, right) => left.length === right.length && left.every((key, index) => key.toLowerCase() === String(right[index] ?? "").toLowerCase());
  const stageIntroChange = (book, summary, hasIntro, sourceReferences) => {
    const id = bookId(book);
    const draft = interiorDraftFor(id, true);
    const originalHasIntro = valueFor(summary, "hasIntro", false);
    const originalSources = persistedIntroSourceReferences(summary);
    if (hasIntro === originalHasIntro) delete draft.hasIntro; else draft.hasIntro = hasIntro;
    if (!hasIntro) delete draft.introSourceReferences;
    else if (sameKeys(sourceReferences, originalSources)) delete draft.introSourceReferences;
    else draft.introSourceReferences = [...sourceReferences];
    trimEmptyInteriorDraft(id, draft);
  };
  const interiorSavePayload = (id) => {
    const draft = interiorDraftFor(id);
    if (!draft) return null;
    const assets = [...draft.assets].map(([sourceReference, change]) => ({ sourceReference, ...(change.active !== undefined ? { active: change.active } : {}), ...(change.frameMode !== undefined ? { frameMode: change.frameMode } : {}) }));
    return { bookId: id, ...(draft.hasBackground !== undefined ? { hasBackground: draft.hasBackground } : {}), ...(draft.hasIntro !== undefined ? { hasIntro: draft.hasIntro } : {}), ...(draft.introSourceReferences !== undefined ? { introSourceReferences: draft.introSourceReferences } : {}), assets };
  };
  const updateInteriorSaveUi = () => {
    const id = state.selectedBookId;
    const dirty = hasInteriorDraft(id);
    const controlsDisabled = processIsActive() || state.bookInteriorSavePending;
    const save = document.querySelector('[data-action="save-book-interior-settings"]');
    if (save) { save.disabled = !dirty || controlsDisabled; save.setAttribute("aria-busy", String(state.bookInteriorSavePending)); save.textContent = state.bookInteriorSavePending ? "Saving…" : "Save changes"; }
    document.querySelectorAll('[data-action="set-book-background"], [data-action="set-interior-active"], [data-action="set-interior-frame-mode"], [data-action="set-intro-mode"], [data-action="intro-add-template"], [data-action="intro-remove-template"], [data-action="intro-move-template"]').forEach((control) => { control.disabled = controlsDisabled || control.dataset.customIntro === "true"; });
    const indicator = document.querySelector("[data-book-interior-unsaved]");
    if (indicator) indicator.hidden = !dirty;
  };
  const localImageMarkup = (asset, alt, fallback = "Preview unavailable") => {
    const url = valueFor(asset, "localImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" width="256" height="256" loading="lazy" decoding="async" data-local-image data-image-fallback="${escapeHtml(fallback)}">`
      : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;
  };
  const introTemplateImageMarkup = (asset, alt, fallback = "Image unavailable") => {
    const url = valueFor(asset, "localImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" width="256" height="256" loading="lazy" decoding="async" data-local-image data-intro-template-id="${escapeHtml(introTemplateAssetId(asset))}" data-image-fallback="${escapeHtml(fallback)}">`
      : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;
  };
  const bookThumbnailMarkup = (book, summary, fallback = "Preview unavailable") => {
    const cover = assetForReference(summary, valueFor(summary, "representativeCoverReference", ""));
    return localImageMarkup(cover, `Cover for ${valueFor(book, "name", bookId(book))}`, fallback);
  };
  const assetDimensions = (asset) => {
    const width = valueFor(asset, "width", null);
    const height = valueFor(asset, "height", null);
    return width && height ? `${width} × ${height}` : "Dimensions unavailable";
  };
  const updateGlobalProcessStatus = () => {
    const control = document.getElementById("global-process-status");
    if (!control) return;
    const snapshot = window.processSnapshot;
    const active = valueFor(snapshot, "isActive", false);
    const cancelling = valueFor(snapshot, "isCancelling", false);
    const step = valueFor(snapshot, "currentStep", "Processing Interior");
    control.classList?.toggle("is-active", active && !cancelling);
    control.classList?.toggle("is-cancelling", cancelling);
    control.innerHTML = `<span class="status-dot"></span><span>${escapeHtml(cancelling ? "Stopping processing" : active ? step : "Nothing processing")}</span>`;
  };

  const renderConfiguration = () => {
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    const setting = (name, fallback) => valueFor(settings, name, fallback);
    const grouped = (group, name, fallback) => valueFor(valueFor(settings, group, {}), name, fallback);
    const detectionInput = (label, name, fallback, extra = "") => `<label class="field"><span>${label}</span><input class="control" data-setting-group="borderLineDetection" data-setting="${name}" type="number" ${extra} value="${grouped("borderLineDetection", name, fallback)}"></label>`;
    content.innerHTML = `<div class="page-header"><div><h1>Configuration</h1><p>Manage global application settings.</p></div><div class="page-actions">${refreshAction("Load")}<button class="button-primary" data-action="save-settings">Save</button></div></div><div class="detail-stack">${panel("Application", `<div class="form-grid two"><label class="field"><span>Maximum concurrency</span><input class="control" data-setting="maximumPageConcurrency" type="number" min="1" max="12" value="${setting("maximumPageConcurrency", 4)}"></label><label class="field"><span>Artwork dark threshold</span><input class="control" data-setting="artworkDetectionThreshold" type="number" min="0" max="255" value="${setting("artworkDetectionThreshold", 20)}"></label></div>`)}${panel("Interior processing", `<div class="form-grid three"><label class="field"><span>Max artwork side</span><input class="control" data-setting="artworkMaximumSide" type="number" min="1" value="${setting("artworkMaximumSide", 2270)}"></label><label class="field"><span>Working width</span><input class="control" data-setting="workingPageWidth" type="number" min="1" value="${setting("workingPageWidth", 2550)}"></label><label class="field"><span>Working height</span><input class="control" data-setting="workingPageHeight" type="number" min="1" value="${setting("workingPageHeight", 2550)}"></label><label class="field"><span>Final width</span><input class="control" data-setting="finalPageWidth" type="number" min="1" value="${setting("finalPageWidth", 2588)}"></label><label class="field"><span>Final height</span><input class="control" data-setting="finalPageHeight" type="number" min="1" value="${setting("finalPageHeight", 2625)}"></label><label class="field"><span>DPI</span><input class="control" data-setting="dpi" type="number" min="1" value="${setting("dpi", 300)}"></label></div>`)}${panel("Advanced artwork detection", `<div class="form-grid three"><label class="field"><span>Normalized source size</span><input class="control" data-setting-group="artworkSourceNormalization" data-setting="normalizedSourceSize" type="number" min="1" value="${grouped("artworkSourceNormalization", "normalizedSourceSize", 2048)}"></label>${detectionInput("Pass 1 depth", "pass1SearchDepth", 200, "min=1")}${detectionInput("Pass 2 depth", "pass2SearchDepth", 320, "min=1")}${detectionInput("Corner padding", "cornerSearchPadding", 40, "min=0")}${detectionInput("Track tolerance", "trackDepthTolerance", 6, "min=0")}${detectionInput("Corner-line tolerance", "cornerLineTolerance", 16, "min=0")}${detectionInput("Max depth spread", "maximumDepthSpread", 24, "min=0")}${detectionInput("Segments", "segmentCount", 8, "min=1")}${detectionInput("Corner exclusion ratio", "cornerExclusionRatio", .10, "min=0 max=1 step=0.01")}${detectionInput("Compatible corners", "minimumCompatibleCorners", 3, "min=1 max=4")}${detectionInput("Min segment support", "minimumSegmentSupportRatio", .35, "min=0 max=1 step=0.01")}${detectionInput("Min side support", "minimumSideSupportRatio", .55, "min=0 max=1 step=0.01")}${detectionInput("Min span", "minimumSpanRatio", .70, "min=0 max=1 step=0.01")}${detectionInput("Supported segments", "minimumSupportedSegments", 6, "min=1")}${detectionInput("Missing segment run", "maximumMissingSegmentRun", 2, "min=0")}</div>`)}${panel("PDF output", `<div class="form-grid two"><label class="field"><span>Interior physical width (inch)</span><input class="control" data-setting="interiorPdfWidthInches" type="number" min="0.1" step="0.1" value="${setting("interiorPdfWidthInches", 8.5)}"></label><label class="field"><span>Interior physical height (inch)</span><input class="control" data-setting="interiorPdfHeightInches" type="number" min="0.1" step="0.1" value="${setting("interiorPdfHeightInches", 8.5)}"></label></div>`)}</div>`;
  };

  const renderBrands = () => {
    const allBrands = valueFor(discovery(), "brands", []);
    if (!state.selectedBrand && allBrands.length) state.selectedBrand = valueFor(allBrands[0], "name", "");
    const selected = allBrands.find((brand) => valueFor(brand, "name", "") === state.selectedBrand);
    const assets = valueFor(selected, "assets", []);
    content.innerHTML = `<div class="page-header"><div><h1>Brands & templates</h1><p>Keep reusable local brand assets ready for each Book.</p></div></div><div class="master-detail"><section class="panel list-panel"><div class="list-title">Brands</div><ul class="item-list">${allBrands.length ? allBrands.map((brand) => `<li class="${valueFor(brand, "name", "") === state.selectedBrand ? "selected" : ""}" data-action="select-brand" data-brand-name="${escapeHtml(valueFor(brand, "name", ""))}"><span>${escapeHtml(valueFor(brand, "name", ""))}</span>${badge((valueFor(brand, "assets", []) ?? []).some((asset) => valueFor(asset, "status", "") === "Missing") ? "Attention" : "Ready")}</li>`).join("") : "<li class=\"empty-row\">No Brands found.</li>"}</ul></section><section class="detail-pane">${selected ? `${panel(escapeHtml(valueFor(selected, "name", "")), `<p class="detail-path">${escapeHtml(valueFor(valueFor(selected, "directory", {}), "value", ""))}</p><div class="brand-asset-grid">${assets.map((asset) => `<div><strong>${escapeHtml(valueFor(asset, "name", ""))}</strong><small>${escapeHtml(valueFor(asset, "type", ""))}</small>${badge(valueFor(asset, "status", "Missing"))}</div>`).join("") || "<p class=\"empty-copy\">No brand assets found.</p>"}</div>`) }${panel("Template settings", `<p class="panel-note">These settings apply only to ${escapeHtml(valueFor(selected, "name", "this Brand"))}.</p><div class="page-actions mt-3"><button class="button-secondary" data-action="load-brand-settings">Load advanced settings</button></div><details class="advanced-settings"><summary>Advanced JSON settings</summary><textarea class="control settings-editor" data-brand-settings>${escapeHtml(state.brandSettings)}</textarea><div class="page-actions mt-3"><button class="button-primary" data-action="save-brand-settings">Save advanced settings</button></div></details>`)} ` : panel("Brand detail", "<p class=\"empty-copy\">Select a Brand to inspect its assets.</p>")}</section></div>`;
  };

  const renderBookTabs = (book, summary) => {
    const checks = valueFor(summary, "validationChecks", []);
    const artifacts = valueFor(summary, "publishedArtifacts", []);
    const pages = valueFor(summary, "interiorPages", []);
    const assets = assetsFor(summary);
    const logs = valueFor(summary, "logs", []);
    const tabButton = (id, label) => `<button class="detail-tab ${state.selectedBookTab === id ? "active" : ""}" data-action="book-tab" data-book-tab="${id}">${label}</button>`;
    let body = "";
    if (state.selectedBookTab === "overview") {
      body = `<section class="book-overview"><div class="summary-grid"><div><span>Status</span>${badge(workspaceStatus(summary))}</div><div><span>Interior preflight</span>${badge(valueFor(summary, "validationStatus", "Checking"))}</div><div><span>Last run</span><strong>${dateTime(valueFor(summary, "lastRunAt", null))}</strong></div><div><span>Pages (interior)</span><strong>${valueFor(summary, "interiorSourcePageCount", 0)}</strong></div></div><p class="panel-note">Use Interior assets to review page previews and choose a frame mode per page.</p></section>`;
    }
    if (state.selectedBookTab === "assets") body = `${renderIntroTemplateWorkspace(book, summary)}<section class="asset-background-setting"><label class="asset-background-toggle"><input type="checkbox" data-action="set-book-background" data-book-id="${escapeHtml(bookId(book))}" ${effectiveBackground(book, summary) ? "checked" : ""} ${processIsActive() || state.bookInteriorSavePending ? "disabled" : ""}> Use Brand background</label><span>Insert the selected Brand background after every active Interior page.</span></section>${renderFolderAssetWorkspace(book, summary)}`;
    if (state.selectedBookTab === "assets" && !body) {
      const matchingAssets = assets.filter((asset) => `${valueFor(asset, "fileName", "")} ${valueFor(asset, "relativePath", "")} ${valueFor(asset, "kind", "")}`.toLowerCase().includes(state.assetFilter.toLowerCase()));
      if (!matchingAssets.some((asset) => valueFor(asset, "sourceReference", "") === state.selectedAssetReference)) state.selectedAssetReference = valueFor(matchingAssets[0], "sourceReference", "");
      const selectedAsset = matchingAssets.find((asset) => valueFor(asset, "sourceReference", "") === state.selectedAssetReference) ?? null;
      const assetRow = (asset) => {
        const reference = valueFor(asset, "sourceReference", "");
        const selected = reference === state.selectedAssetReference;
        return `<button type="button" class="asset-row ${selected ? "selected" : ""}" data-action="select-asset" data-source-reference="${escapeHtml(reference)}" aria-pressed="${selected}"><span class="asset-thumb">${localImageMarkup(asset, "")}</span><span class="asset-row-copy"><strong>${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}</strong><small>${escapeHtml(valueFor(asset, "relativePath", ""))}</small></span><span>${badge(valueFor(asset, "kind", "Asset"))}</span></button>`;
      };
      const gridCard = (asset) => {
        const reference = valueFor(asset, "sourceReference", "");
        const selected = reference === state.selectedAssetReference;
        return `<button type="button" class="asset-card ${selected ? "selected" : ""}" data-action="select-asset" data-source-reference="${escapeHtml(reference)}" aria-pressed="${selected}"><span class="asset-card-preview">${localImageMarkup(asset, "")}</span><strong>${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}</strong><small>${escapeHtml(assetDimensions(asset))} · ${escapeHtml(valueFor(asset, "kind", "Asset"))}</small></button>`;
      };
      const inspector = selectedAsset ? `<section class="asset-inspector" aria-label="Selected asset inspector"><div class="asset-inspector-preview">${localImageMarkup(selectedAsset, `Preview of ${valueFor(selectedAsset, "fileName", "selected asset")}`)}</div><h3>${escapeHtml(valueFor(selectedAsset, "fileName", "Selected asset"))}</h3><dl><div><dt>Folder</dt><dd>${escapeHtml(valueFor(selectedAsset, "folder", "Unknown"))}</dd></div><div><dt>Dimensions</dt><dd>${escapeHtml(assetDimensions(selectedAsset))}</dd></div><div><dt>Frame mode</dt><dd>${escapeHtml(frameModeValue(valueFor(selectedAsset, "frameMode", "auto")))}</dd></div><div><dt>Path</dt><dd>${escapeHtml(valueFor(selectedAsset, "relativePath", ""))}</dd></div></dl></section>` : `<section class="asset-inspector"><p class="empty-copy">Select an asset to inspect its local metadata.</p></section>`;
      body = `<section class="asset-workspace"><aside class="asset-source-panel"><h3>Source folders</h3><p>Assets stay local. Previews are requested only when selected.</p><div class="asset-folder-count"><span>Interior</span><strong>${assets.filter((asset) => valueFor(asset, "kind", "") === "Interior").length}</strong></div><div class="asset-folder-count"><span>Cover candidates</span><strong>${assets.filter((asset) => valueFor(asset, "kind", "") === "Cover").length}</strong></div></aside><section class="asset-browser"><div class="asset-browser-toolbar"><div><h3 class="panel-title">Asset Workspace</h3><p class="panel-note">Review local files before processing.</p></div><div class="asset-view-toggle" aria-label="Asset view"><button class="${state.assetView === "list" ? "active" : ""}" data-action="asset-view" data-asset-view="list" aria-pressed="${state.assetView === "list"}">List</button><button class="${state.assetView === "grid" ? "active" : ""}" data-action="asset-view" data-asset-view="grid" aria-pressed="${state.assetView === "grid"}">Grid</button></div></div><label class="field mt-4"><span>Search assets</span><input class="control" data-action="filter-assets" value="${escapeHtml(state.assetFilter)}" placeholder="File name or path"></label><p class="asset-result-count">${matchingAssets.length} of ${assets.length} local assets</p><div class="${state.assetView === "grid" ? "asset-grid" : "asset-list"}">${matchingAssets.length ? matchingAssets.map(state.assetView === "grid" ? gridCard : assetRow).join("") : "<p class=\"empty-copy\">No assets match this search.</p>"}</div></section>${inspector}</section>`;
    }
    if (state.selectedBookTab === "validation") {
      const activeChecks = checks.filter((check) => !valueFor(check, "code", "").startsWith("book.cover_"));
      const successful = activeChecks.filter((check) => valueFor(check, "isSuccess", false) && !valueFor(check, "isWarning", false)).length;
      const informational = activeChecks.filter((check) => valueFor(check, "isWarning", false)).length;
      const failing = activeChecks.length - successful - informational;
      const checkRow = (check) => {
        const warning = valueFor(check, "isWarning", false);
        const success = valueFor(check, "isSuccess", false);
        const code = valueFor(check, "code", "validation.unknown");
        const recovery = !success
          ? `<div class="validation-actions"><button class="button-secondary" data-action="refresh">Refresh local files</button><button class="button-secondary" data-action="validate-book" data-book-id="${escapeHtml(bookId(book))}">Retry Interior preflight</button></div>`
          : "";
        return `<li class="validation-item ${warning ? "warning" : success ? "success" : "failure"}"><span class="validation-icon" aria-hidden="true">${warning ? "Info" : success ? "Ready" : "Action"}</span><div><strong>${escapeHtml(code.replaceAll(".", " "))}</strong><p>${escapeHtml(valueFor(check, "message", ""))}</p>${recovery}</div></li>`;
      };
      body = `<section class="validation-workspace"><p class="panel-note">Interior-only preflight checks the source pages that will be processed. Cover validation is not part of this workflow.</p><div class="validation-summary" role="alert" tabindex="-1"><div><span>Ready to process</span><strong>${successful}</strong></div><div><span>Needs attention</span><strong>${failing}</strong></div><div><span>Informational</span><strong>${informational}</strong></div></div><ul class="validation-list">${activeChecks.length ? activeChecks.map(checkRow).join("") : "<li class=\"empty-row\">No Interior validation result yet. Refresh local files to begin.</li>"}</ul></section>`;
    }
    if (state.selectedBookTab === "processing") body = panel("Processing", `<dl class="summary-grid"><div><span>Workspace</span>${badge(workspaceStatus(summary))}</div><div><span>Current step</span><strong>${escapeHtml(valueFor(summary, "currentStep", "Not started"))}</strong></div><div><span>Cached processed pages</span><strong>${pages.length} / ${valueFor(summary, "interiorSourcePageCount", 0)}</strong></div></dl>${pages.length ? `<table class="data-table mt-4"><thead><tr><th>Page</th><th>Status</th><th>Final page</th></tr></thead><tbody>${pages.map((page) => `<tr><td>${escapeHtml(valueFor(page, "pageId", ""))}</td><td>${badge(valueFor(page, "status", ""))}</td><td class="detail-path">${escapeHtml(String(valueFor(page, "finalPagePath", "")).split(/[\\/]/).pop())}</td></tr>`).join("")}</tbody></table>` : "<p class=\"empty-copy mt-4\">No processed page cache is currently retained.</p>"}`);
    if (state.selectedBookTab === "outputs") body = panel("Published outputs", artifacts.length ? `<ul class="artifact-list">${artifacts.map((artifact) => `<li>${escapeHtml(String(artifact).split(/[\\/]/).pop())}</li>`).join("")}</ul>` : "<p class=\"empty-copy\">No published output yet.</p>");
    if (state.selectedBookTab === "logs") body = panel("Workspace logs", logs.length ? `<table class="data-table"><thead><tr><th>Time</th><th>Event</th><th>Detail</th></tr></thead><tbody>${logs.map((log) => `<tr><td>${dateTime(valueFor(log, "timestamp", null))}</td><td>${escapeHtml(valueFor(log, "eventName", ""))}</td><td>${escapeHtml(valueFor(log, "detail", ""))}</td></tr>`).join("")}</tbody></table>` : "<p class=\"empty-copy\">No workspace log entries yet.</p>");
    const readiness = processingReadiness(book, summary);
    return `<div class="book-heading"><div><h2>${escapeHtml(valueFor(book, "name", ""))}</h2><p>Interior-only production workspace</p></div><div class="page-actions"><button class="button-secondary" data-action="validate-book" data-book-id="${escapeHtml(bookId(book))}">Run Interior preflight</button><button class="button-primary" data-action="queue-selected-book" ${readiness.ready ? "" : "disabled"} title="${escapeHtml(readiness.reason)}">Process Interior</button></div></div><nav class="detail-tabs">${tabButton("overview", "Overview")}${tabButton("assets", `Interior assets (${assets.filter((asset) => valueFor(asset, "kind", "") === "Interior").length})`)}${tabButton("validation", "Validation")}${tabButton("processing", "Processing")}${tabButton("outputs", "Outputs")}${tabButton("logs", "Logs")}</nav><div class="tab-body">${body}</div>`;
  };

  const renderIntroTemplateWorkspace = (book, summary) => {
    const brand = activeBrand();
    const allTemplates = valueFor(brand, "introTemplateAssets", []) ?? [];
    const templates = allTemplates.filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", "")));
    const selection = effectiveIntro(book, summary);
    const candidates = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    const byReference = new Map(candidates.map((asset) => [String(valueFor(asset, "sourceReference", "")).toLowerCase(), asset]));
    const selected = selection.sourceReferences.map((reference) => byReference.get(String(reference).toLowerCase())).filter(Boolean);
    const available = candidates.filter((asset) => !selection.sourceReferences.some((reference) => String(reference).toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase()));
    const disabled = processIsActive() || state.bookInteriorSavePending;
    const readiness = introReadiness(book, summary);
    const selectedTile = (asset, index) => {
      const reference = valueFor(asset, "sourceReference", "");
      return `<article class="intro-template-tile"><span class="intro-template-preview">${localImageMarkup(asset, `Custom Intro ${valueFor(asset, "fileName", "")}`, "Image unavailable")}</span><strong>Intro #${index + 1}</strong><span title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</span><div class="intro-template-actions"><button class="button-secondary" data-action="intro-move-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-index="${index}" data-intro-direction="up" aria-label="Move ${escapeHtml(valueFor(asset, "fileName", ""))} earlier" ${index === 0 || disabled ? "disabled" : ""}>Earlier</button><button class="button-secondary" data-action="intro-move-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-index="${index}" data-intro-direction="down" aria-label="Move ${escapeHtml(valueFor(asset, "fileName", ""))} later" ${index === selected.length - 1 || disabled ? "disabled" : ""}>Later</button><button class="button-secondary" data-action="intro-remove-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-source-reference="${escapeHtml(reference)}" ${disabled ? "disabled" : ""}>Remove</button></div></article>`;
    };
    const availableOption = (asset) => `<button class="intro-template-add" data-action="intro-add-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-source-reference="${escapeHtml(valueFor(asset, "sourceReference", ""))}" ${disabled ? "disabled" : ""}>${localImageMarkup(asset, `Available Book interior page ${valueFor(asset, "fileName", "")}`, "Image unavailable")}<span>${escapeHtml(valueFor(asset, "fileName", ""))}</span><small>Add as Intro</small></button>`;
    const brandCopy = brand ? `${escapeHtml(valueFor(brand, "name", ""))} · ${templates.length} eligible local template${templates.length === 1 ? "" : "s"}` : "Choose a Brand in the header to see local templates.";
    const automaticPreview = templates.length
      ? `<div class="intro-template-grid">${templates.map((asset) => `<article class="intro-template-tile"><span class="intro-template-preview">${introTemplateImageMarkup(asset, `Automatic Intro template ${valueFor(asset, "fileName", "")}`)}</span><strong>Automatic</strong><span title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</span></article>`).join("")}</div>`
      : "";
    return `<section class="intro-template-workspace"><div class="intro-template-heading"><div><h3>Intro pages</h3><p>${selection.hasIntro ? "Choose ordered pages from this Book's Book interior. Selected pages are removed from the normal Interior block." : brandCopy}</p></div><span class="status-badge ${selection.hasIntro ? "status-warn" : "status-muted"}">${selection.hasIntro ? "Custom Book interior" : "Automatic Brand template"}</span></div><p class="${readiness.ready ? "panel-note" : "intro-template-warning"}" role="${readiness.ready ? "status" : "alert"}">${readiness.ready ? "Ready for backend size validation during processing." : escapeHtml(readiness.reason)}</p><fieldset class="intro-mode-choice" ${disabled ? "disabled" : ""}><legend>Intro source</legend><label><input type="radio" name="intro-mode" data-action="set-intro-mode" data-book-id="${escapeHtml(bookId(book))}" value="auto" ${selection.hasIntro ? "" : "checked"}> Automatic <small>Use every eligible current Brand IntroTemplate in filename order.</small></label><label><input type="radio" name="intro-mode" data-action="set-intro-mode" data-book-id="${escapeHtml(bookId(book))}" value="custom" ${selection.hasIntro ? "checked" : ""}> Custom <small>Choose Book interior pages and their print order.</small></label></fieldset>${selection.hasIntro ? `<div class="intro-template-selection"><div><h4>Selected Book interior order</h4><p>${selected.length ? "Selected pages run as Intro and cannot also enter the Interior shuffle." : "Add at least one Book interior page to make this Book ready."}</p></div><div class="intro-template-grid">${selected.length ? selected.map(selectedTile).join("") : "<p class=\"empty-copy\">No Book interior pages selected.</p>"}</div>${available.length ? `<div class="intro-template-available"><h4>Add Book interior pages</h4><div class="intro-template-add-grid">${available.map(availableOption).join("")}</div></div>` : ""}</div>` : `<div class="intro-template-selection"><h4>Automatic Brand IntroTemplate</h4><p class="panel-note">All eligible templates are processed in filename order. Book interior pages remain eligible for normal Interior processing.</p>${automaticPreview}</div>`}</section>`;
  };

  const renderFolderAssetWorkspace = (book, summary) => {
    const allAssets = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    const sourceFolderNames = valueFor(summary, "sourceFolders", []).map((folder) => valueFor(folder, "name", "")).filter(Boolean);
    const folderFor = (asset) => sourceFolderNames.find((name) => valueFor(asset, "relativePath", "").replaceAll("\\", "/").toLowerCase().startsWith(`${name.toLowerCase()}/`)) ?? valueFor(asset, "folder", "Other");
    const folderNames = [...new Set(allAssets.map(folderFor))].sort((left, right) => left.localeCompare(right));
    if (state.assetFolder !== "All folders" && !folderNames.includes(state.assetFolder)) state.assetFolder = "All folders";
    const matching = allAssets.filter((asset) => `${valueFor(asset, "fileName", "")} ${valueFor(asset, "relativePath", "")}`.toLowerCase().includes(state.assetFilter.toLowerCase()) && (state.assetFolder === "All folders" || folderFor(asset) === state.assetFolder));
    const matchingFolderNames = folderNames.filter((name) => matching.some((asset) => folderFor(asset) === name));
    const tile = (asset) => {
      const settings = effectiveInteriorAsset(book, asset);
      const mode = settings.frameMode;
      const reference = valueFor(asset, "sourceReference", "");
      const active = settings.isActive;
      const intro = effectiveIntro(book, summary);
      const introIndex = intro.hasIntro ? intro.sourceReferences.findIndex((item) => item.toLowerCase() === reference.toLowerCase()) : -1;
      const selectedAsIntro = introIndex >= 0;
      const disabled = processIsActive() || state.bookInteriorSavePending || selectedAsIntro ? "disabled" : "";
      const customIntro = selectedAsIntro ? 'data-custom-intro="true"' : "";
      return `<article class="folder-asset-item ${active ? "" : "is-inactive"} ${selectedAsIntro ? "is-custom-intro" : ""}" data-source-reference="${escapeHtml(reference)}"><div class="folder-asset-tile"><span class="folder-asset-preview">${localImageMarkup(asset, `Preview of ${valueFor(asset, "fileName", "asset")}`, "Image unavailable")}</span><strong title="${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}">${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}</strong><small>${escapeHtml(assetDimensions(asset))}</small>${selectedAsIntro ? `<span class="asset-intro-badge">Intro #${introIndex + 1}</span>` : ""}<label class="asset-active-toggle"><input type="checkbox" data-action="set-interior-active" data-book-id="${escapeHtml(bookId(book))}" data-source-reference="${escapeHtml(reference)}" ${active ? "checked" : ""} ${customIntro} ${disabled}> Active</label><div class="asset-frame-review"><label><span>Frame mode</span><select class="control h-8" data-action="set-interior-frame-mode" data-book-id="${escapeHtml(bookId(book))}" data-source-reference="${escapeHtml(reference)}" ${customIntro} ${disabled}><option value="auto" ${mode === "auto" ? "selected" : ""}>Auto</option><option value="enabled" ${mode === "enabled" ? "selected" : ""}>Frame</option><option value="disabled" ${mode === "disabled" ? "selected" : ""}>No frame</option></select></label></div></div></article>`;
    };
    const group = (name) => {
      const items = matching.filter((asset) => folderFor(asset) === name);
      return `<section class="asset-folder-group"><header><div><h3>${escapeHtml(name)}</h3><span>${items.length} Interior page(s)</span></div></header>${items.length ? `<div class="folder-asset-grid">${items.map(tile).join("")}</div>` : ""}</section>`;
    };
    const activeCount = allAssets.filter((asset) => effectiveInteriorAsset(book, asset).isActive).length;
    return `<section class="folder-asset-workspace"><div class="asset-browser-toolbar"><div><h3 class="panel-title">Interior assets</h3><p class="panel-note">Choose exactly which pages will be processed and set a frame mode for each active page.</p></div></div><div class="asset-folder-filter" role="group" aria-label="Interior asset folder filters"><button class="${state.assetFolder === "All folders" ? "active" : ""}" data-action="asset-folder" data-asset-folder="All folders" aria-pressed="${state.assetFolder === "All folders"}">All Interior (${allAssets.length})</button>${folderNames.map((name) => `<button class="${state.assetFolder === name ? "active" : ""}" data-action="asset-folder" data-asset-folder="${escapeHtml(name)}" aria-pressed="${state.assetFolder === name}">${escapeHtml(name)} (${allAssets.filter((asset) => folderFor(asset) === name).length})</button>`).join("")}</div><label class="field asset-search-field"><span>Search Interior assets</span><input class="control" data-action="filter-assets" value="${escapeHtml(state.assetFilter)}" placeholder="File name or source-relative path"></label><p class="asset-result-count" role="status">${matching.length} shown · ${activeCount} of ${allAssets.length} active</p><div class="folder-asset-layout"><div class="folder-asset-groups">${matching.length ? matchingFolderNames.map(group).join("") : "<p class=\"empty-copy\">No Interior pages match this search.</p>"}</div></div></section>`;
  };

  const renderBookDrawer = (book, summary) => {
    if (!state.bookDrawerOpen || !book || !summary) return "";
    const cover = assetForReference(summary, valueFor(summary, "representativeCoverReference", ""));
    const dirty = hasInteriorDraft(bookId(book));
    const saveDisabled = !dirty || processIsActive() || state.bookInteriorSavePending;
    return `<div class="book-drawer-layer"><section class="book-drawer" role="dialog" aria-labelledby="book-drawer-title"><header class="book-drawer-header"><span class="book-drawer-preview">${localImageMarkup(cover, `Cover for ${valueFor(book, "name", "")}`)}</span><div><p class="eyebrow">Book detail</p><h2 id="book-drawer-title" tabindex="-1">${escapeHtml(valueFor(book, "name", ""))}</h2><div>${badge(productionStatus(summary, book))} ${badge(bookFrameState(summary))}</div></div><div class="book-drawer-actions"><span data-book-interior-unsaved role="status" ${dirty ? "" : "hidden"}>Unsaved changes</span><button class="button-primary" data-action="save-book-interior-settings" data-book-id="${escapeHtml(bookId(book))}" ${saveDisabled ? "disabled" : ""} aria-busy="${state.bookInteriorSavePending}">${state.bookInteriorSavePending ? "Saving…" : "Save changes"}</button><button class="button-secondary" data-action="close-book-drawer" aria-label="Close Book detail">Close</button></div></header><div class="book-drawer-body">${renderBookTabs(book, summary)}</div></section></div>`;
  };

  const renderBooks = () => {
    const existingDrawerBody = document.querySelector(".book-drawer-body");
    if (existingDrawerBody && Number.isFinite(existingDrawerBody.scrollTop)) state.bookDrawerScrollTop = existingDrawerBody.scrollTop;
    const activeElement = document.activeElement;
    if (activeElement?.dataset.action === "filter-assets") {
      state.assetSearchFocused = true;
      state.assetSearchCaret = activeElement.selectionStart ?? activeElement.value.length;
    }
    const allBooks = books();
    const statuses = ["All", "Needs review", "Ready", "Processing", "PDF ready", "Failed"];
    const statusCounts = statuses.map((name) => ({ name, count: name === "All" ? allBooks.length : allBooks.filter((book) => productionStatus(summaryFor(book), book) === name).length }));
    const filtered = allBooks.filter((book) => {
      const summary = summaryFor(book);
      return valueFor(book, "name", "").toLowerCase().includes(state.bookFilter.toLowerCase()) &&
        (state.bookStatus === "All" || productionStatus(summary, book) === state.bookStatus);
    }).sort((left, right) => {
      const leftSummary = summaryFor(left);
      const rightSummary = summaryFor(right);
      if (state.bookSort === "name") return valueFor(left, "name", "").localeCompare(valueFor(right, "name", ""));
      return new Date(valueFor(rightSummary, "lastRunAt", 0)).getTime() - new Date(valueFor(leftSummary, "lastRunAt", 0)).getTime();
    });
    const pageSize = 12;
    const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
    state.bookPage = Math.min(Math.max(1, state.bookPage), totalPages);
    const pageItems = filtered.slice((state.bookPage - 1) * pageSize, state.bookPage * pageSize);
    if (!pageItems.some((item) => bookId(item) === state.selectedBookId)) state.selectedBookId = pageItems[0] ? bookId(pageItems[0]) : "";
    const card = (item) => {
      const itemSummary = summaryFor(item);
      const id = bookId(item);
      const thumbnail = bookThumbnailMarkup(item, itemSummary);
      const total = valueFor(itemSummary, "interiorSourcePageCount", 0);
      const interiorAssets = assetsFor(itemSummary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
      const active = interiorAssets.length ? interiorAssets.filter((asset) => effectiveInteriorAsset(item, asset).isActive).length : valueFor(itemSummary, "activeInteriorSourcePageCount", total);
      const status = productionStatus(itemSummary, item);
      return `<article class="book-card ${id === state.selectedBookId ? "selected" : ""}"><button type="button" class="book-card-main" data-action="select-book" data-book-id="${escapeHtml(id)}" aria-label="Open ${escapeHtml(valueFor(item, "name", ""))}"><span class="book-card-preview">${thumbnail}</span><span class="book-card-copy"><strong title="${escapeHtml(valueFor(item, "name", ""))}">${escapeHtml(valueFor(item, "name", ""))}</strong><small>${active} / ${total} Interior active</small><span>${badge(status)} ${badge(bookFrameState(itemSummary))}</span></span></button><footer><label><input type="checkbox" aria-label="Queue ${escapeHtml(valueFor(item, "name", ""))}" data-action="queue-book" data-book-id="${escapeHtml(id)}" ${state.selectedBookIds.has(id) ? "checked" : ""}> Queue</label><button class="button-secondary book-card-action" data-action="select-book" data-book-id="${escapeHtml(id)}">${status === "Ready" ? "Review files" : status === "Processing" ? "View process" : "Preflight"}</button></footer></article>`;
    };
    const start = filtered.length ? (state.bookPage - 1) * pageSize + 1 : 0;
    const end = Math.min(state.bookPage * pageSize, filtered.length);
    content.innerHTML = `<section class="book-library-page"><div class="page-header"><div><h1>Books</h1><p>Filter local Books, validate only what needs review, and send selected Books to Interior Processing.</p></div><div class="page-actions">${refreshAction()}<button class="button-secondary" data-action="clear-cache" ${cacheCleanupBlocked() ? "disabled" : ""}>${state.cacheCleanupActive ? "Clearing…" : "Clear Cache"}</button><button class="button-secondary" data-action="validate-all">Validate all</button><button class="button-primary" data-action="go-process">Process Interior</button></div></div><section class="book-toolbar"><label class="field"><span>Search books</span><input class="control" data-action="filter-books" value="${escapeHtml(state.bookFilter)}" placeholder="Book name"></label><label class="field"><span>Sort</span><select class="control" data-action="book-sort"><option value="activity" ${state.bookSort === "activity" ? "selected" : ""}>Last activity</option><option value="name" ${state.bookSort === "name" ? "selected" : ""}>Book name</option></select></label><div class="asset-view-toggle" aria-label="Book view"><button class="${state.bookView === "grid" ? "active" : ""}" data-action="book-view" data-book-view="grid" aria-pressed="${state.bookView === "grid"}">Grid</button><button class="${state.bookView === "list" ? "active" : ""}" data-action="book-view" data-book-view="list" aria-pressed="${state.bookView === "list"}">Compact list</button></div></section><div class="status-filters" role="group" aria-label="Book status filters">${statusCounts.map(({ name, count }) => `<button class="${state.bookStatus === name ? "active" : ""}" data-action="book-status" data-book-status="${name}" aria-pressed="${state.bookStatus === name}">${name}<strong>${count}</strong></button>`).join("")}</div><section class="book-secondary-filters"><button class="button-secondary" data-action="clear-book-filters">Clear filters</button><span class="book-filter-result" role="status" aria-atomic="true">${filtered.length} Books match the active filters.</span></section><section class="book-library-results"><div class="book-library-grid-scroll"><section class="${state.bookView === "grid" ? "book-grid" : "book-compact-list"}">${pageItems.length ? pageItems.map(card).join("") : `<div class="book-grid-empty"><strong>No Books match this view.</strong><span>Clear a filter or refresh the local source folders.</span></div>`}</section></div><footer class="book-pagination" data-book-total-pages="${totalPages}"><span>${start}–${end} of ${filtered.length}</span><div><button class="button-secondary" data-action="book-page" data-book-page="first" ${state.bookPage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="book-page" data-book-page="previous" ${state.bookPage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.bookPage} of ${totalPages}</span><button class="button-secondary" data-action="book-page" data-book-page="next" ${state.bookPage === totalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="book-page" data-book-page="last" ${state.bookPage === totalPages ? "disabled" : ""}>Last</button></div><button class="button-primary" data-action="go-process" ${state.selectedBookIds.size ? "" : "disabled"}>Process ${state.selectedBookIds.size} selected</button></footer></section></section>`;
    const selected = selectedBook();
    if (selected) content.insertAdjacentHTML("beforeend", renderBookDrawer(selected, summaryFor(selected)));
    if (state.drawerFocusTitle) {
      state.drawerFocusTitle = false;
      document.getElementById("book-drawer-title")?.focus();
    }
    if (state.restoreBookFocus) {
      state.restoreBookFocus = false;
      document.querySelector(`[data-action="select-book"][data-book-id="${CSS.escape(state.selectedBookId)}"]`)?.focus();
    }
    const drawerBody = document.querySelector(".book-drawer-body");
    if (drawerBody && Number.isFinite(state.bookDrawerScrollTop)) drawerBody.scrollTop = state.bookDrawerScrollTop;
    if (state.assetSearchFocused) {
      const search = document.querySelector("[data-action=\"filter-assets\"]");
      if (search) {
        search.focus();
        search.setSelectionRange(state.assetSearchCaret, state.assetSearchCaret);
      }
      state.assetSearchFocused = false;
    }
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
    const blockedSelection = [...state.selectedBookIds]
      .map((id) => books().find((book) => bookId(book) === id))
      .filter(Boolean)
      .map((book) => ({ book, readiness: processingReadiness(book, summaryFor(book)) }))
      .find((item) => !item.readiness.ready);
    const queue = hasSession ? sessionQueue : pendingQueue;
    const currentBook = valueFor(valueFor(session, "currentBookId", {}), "value", terminal ? "Last session" : "No active Book");
    const terminalStatuses = sessionQueue.map((entry) => displayStatus(valueFor(entry, "status", "Not started")));
    const derivedTerminalStep = terminalStatuses.includes("Failed")
      ? "Failed"
      : terminalStatuses.includes("Cancelled")
        ? "Cancelled"
        : "Completed";
    const currentStep = valueFor(session, "currentStep", null) || (terminal ? derivedTerminalStep : "Waiting");
    const completed = valueFor(session, "pagesCompleted", 0);
    const total = valueFor(session, "pagesTotal", 0);
    const percent = total ? Math.min(100, Math.round((completed / total) * 100)) : 0;
    const stage = cancelling ? "Cancelling" : terminal ? derivedTerminalStep : currentStep;
    const stages = ["Preparing", "Processing", "PDF export"];
    const currentStageIndex = stage === "Processing" ? 1 : stage === "PDF export" ? 2 : 0;
    const failureDetails = queue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Failed" && valueFor(entry, "detail", null));
    content.innerHTML = `<div class="page-header"><div><h1>Process Interior</h1><p>${cancelling ? "Stopping Interior Processing session…" : active ? "Active Interior Processing session" : terminal ? "Last Interior Processing session" : "Prepare a selected interior-only book queue."}</p></div>${active ? cancelling ? '<button class="button-danger" disabled>Stopping processing…</button>' : '<button class="button-danger" data-action="cancel-process">Cancel session</button>' : ""}</div><section class="process-status-strip" aria-live="polite"><div><span>Session state</span><strong>${escapeHtml(stage)}</strong></div><div><span>Elapsed</span><strong>${elapsedTime(valueFor(session, "startedAt", null))}</strong></div><div><span>Workers</span><strong>${valueFor(session, "workerLimit", 0) || "—"}</strong></div><div><span>Progress</span><strong>${completed} / ${total || "?"} pages</strong></div></section><ol class="process-stages">${stages.map((item, index) => `<li class="${index < currentStageIndex ? "complete" : index === currentStageIndex && active ? "active" : ""}"><span>${index + 1}</span>${item}</li>`).join("")}</ol><div class="process-grid">${panel(hasSession ? (terminal ? "Last session" : "Queue") : "Selected queue", `<ul class="queue-list">${queue.length ? queue.map((entry) => `<li><span>${escapeHtml(valueFor(valueFor(entry, "bookId", {}), "value", ""))}</span>${badge(valueFor(entry, "status", "NotStarted"))}<small>${escapeHtml(valueFor(entry, "detail", "Waiting"))}</small></li>`).join("") : "<li class=\"empty-row\">Select Books on the Books page.</li>"}</ul>`)}${panel("Current stage", `<div class="process-book"><strong>${escapeHtml(currentBook)}</strong><span>${escapeHtml(currentStep)}</span></div><div class="progress-track"><span style="width:${percent}%"></span></div><p class="progress-copy">${completed} / ${total || "?"} pages · ${valueFor(session, "workerLimit", 0) || "?"} workers</p>${blockedSelection ? `<div class="process-failure" role="alert"><strong>Selected Book needs review</strong><p>${escapeHtml(valueFor(blockedSelection.book, "name", "Book"))}: ${escapeHtml(blockedSelection.readiness.reason)}</p></div>` : ""}${failureDetails.length ? `<div class="process-failure" role="alert"><strong>Run needs review</strong>${failureDetails.map((entry) => `<p>${escapeHtml(valueFor(valueFor(entry, "bookId", {}), "value", ""))}: ${escapeHtml(valueFor(entry, "detail", ""))}</p>`).join("")}</div>` : ""}<div class="page-actions mt-4">${active || cancelling ? "" : `<button class="button-primary" data-action="start-process" ${state.selectedBookIds.size && !blockedSelection ? "" : "disabled"}>${terminal ? "Start New Interior Processing" : "Start Interior Processing"}</button>`}</div>`)}</div>`;
    if (requestProcess) send("process.get");
  };

  const renderOutputs = () => {
    const library = pdfLibraryBooks();
    const eligibleTotal = eligiblePdfLibraryBooks().length;
    const totalPages = Math.max(1, Math.ceil(library.length / pdfLibraryPageSize));
    state.pdfLibraryPage = Math.min(Math.max(1, state.pdfLibraryPage), totalPages);
    const pageStart = (state.pdfLibraryPage - 1) * pdfLibraryPageSize;
    const pageItems = library.slice(pageStart, pageStart + pdfLibraryPageSize);
    const start = library.length ? pageStart + 1 : 0;
    const end = Math.min(pageStart + pdfLibraryPageSize, library.length);
    const actions = (summary, output, compact = false) => `<div class="output-actions"><button class="button-primary" data-action="open-output" data-book-id="${escapeHtml(valueFor(valueFor(summary, "bookId", {}), "value", ""))}" data-artifact-reference="${escapeHtml(valueFor(output, "artifactReference", ""))}">${compact ? "Open" : "Open PDF"}</button><button class="button-secondary" data-action="reveal-output" data-book-id="${escapeHtml(valueFor(valueFor(summary, "bookId", {}), "value", ""))}" data-artifact-reference="${escapeHtml(valueFor(output, "artifactReference", ""))}">${compact ? "Reveal" : "Reveal in Explorer"}</button><button class="button-secondary" data-action="copy-output-path" data-book-id="${escapeHtml(valueFor(valueFor(summary, "bookId", {}), "value", ""))}" data-artifact-reference="${escapeHtml(valueFor(output, "artifactReference", ""))}">${compact ? "Copy" : "Copy path"}</button></div>`;
    const outputRow = (summary, output) => {
      const pageCount = valueFor(output, "pageCount", "—");
      const dimensions = valueFor(output, "widthInches", null) ? `${valueFor(output, "widthInches", 0)} × ${valueFor(output, "heightInches", 0)} in` : "—";
      return `<li class="pdf-library-file"><div class="pdf-library-file-mark">PDF</div><div class="pdf-library-file-copy"><div class="pdf-library-file-title"><strong>${escapeHtml(valueFor(output, "fileName", "PDF output"))}</strong>${badge(valueFor(output, "verificationStatus", "Available"))}</div><small>${escapeHtml(String(pageCount))} pages · ${escapeHtml(dimensions)} · ${fileSize(valueFor(output, "fileSizeBytes", 0))}</small>${actions(summary, output, state.pdfLibraryView === "grid")}</div></li>`;
    };
    const bookCard = ({ book, summary }) => {
      const name = pdfLibraryBookName(book, summary);
      const thumbnail = bookThumbnailMarkup(book, summary, "Cover unavailable");
      const outputs = valueFor(summary, "outputSummaries", []);
      const totalBytes = pdfLibraryOutputSize(summary);
      const generatedAt = pdfLibraryGeneratedAt(summary);
      return `<article class="pdf-library-book pdf-library-book-${state.pdfLibraryView}" data-pdf-book-id="${escapeHtml(name)}"><span class="pdf-library-book-preview">${thumbnail}</span><header class="pdf-library-book-header"><div><div class="pdf-library-title-row"><h2>${escapeHtml(name)}</h2><span class="status-badge status-good">PDF ready</span></div><p>${outputs.length} ${outputs.length === 1 ? "PDF" : "PDFs"} · ${fileSize(totalBytes)} · ${dateTime(generatedAt ? new Date(generatedAt).toISOString() : null)}</p></div></header><ul class="pdf-library-files">${outputs.map((output) => outputRow(summary, output)).join("")}</ul></article>`;
    };
    const empty = eligibleTotal === 0
      ? `<section class="pdf-library-empty"><strong>No completed PDFs yet.</strong><p>Process a Book to make its final PDF appear here.</p></section>`
      : `<section class="pdf-library-empty"><strong>No PDF Books match your search.</strong><p>Try a different Book name.</p></section>`;
    const pagination = library.length ? `<footer class="book-pagination pdf-library-pagination" data-pdf-library-total-pages="${totalPages}"><span>${start}–${end} of ${library.length}</span><div><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="first" ${state.pdfLibraryPage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="previous" ${state.pdfLibraryPage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.pdfLibraryPage} of ${totalPages}</span><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="next" ${state.pdfLibraryPage === totalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="last" ${state.pdfLibraryPage === totalPages ? "disabled" : ""}>Last</button></div></footer>` : "";
    content.innerHTML = `<div class="page-header"><div><h1>PDF Library</h1><p>Completed Books with local PDF output.</p></div></div><div class="pdf-library-toolbar"><label class="field"><span>Search Books</span><input class="control" type="search" value="${escapeHtml(state.pdfLibrarySearch)}" placeholder="Search Books..." data-action="pdf-library-search"></label><label class="field pdf-library-sort"><span>Sort</span><select class="control" data-action="pdf-library-sort"><option value="newest" ${state.pdfLibrarySort === "newest" ? "selected" : ""}>Newest</option><option value="name" ${state.pdfLibrarySort === "name" ? "selected" : ""}>Name</option><option value="size" ${state.pdfLibrarySort === "size" ? "selected" : ""}>Size</option></select></label><div class="asset-view-toggle" aria-label="PDF Library view"><button class="${state.pdfLibraryView === "grid" ? "active" : ""}" data-action="pdf-library-view" data-pdf-library-view="grid" aria-pressed="${state.pdfLibraryView === "grid"}">Grid</button><button class="${state.pdfLibraryView === "list" ? "active" : ""}" data-action="pdf-library-view" data-pdf-library-view="list" aria-pressed="${state.pdfLibraryView === "list"}">List</button></div><span class="pdf-library-result-count">${library.length} ${library.length === 1 ? "Book" : "Books"}</span></div>${library.length ? `<section class="${state.pdfLibraryView === "grid" ? "pdf-library-grid" : "pdf-library-list"}">${pageItems.map(bookCard).join("")}</section>` : empty}${pagination}`;
    if (state.pdfLibrarySearchFocused) {
      const input = content.querySelector('[data-action="pdf-library-search"]');
      if (input) {
        input.focus();
        input.setSelectionRange?.(state.pdfLibrarySearchCaret, state.pdfLibrarySearchCaret);
      }
    }
  };

  const diagnosticsTabValue = (value) => ["summary", "tasks", "performance", "book"].includes(value) ? value : "summary";

  const renderDiagnosticsTabs = () => {
    const tabs = [["summary", "Summary"], ["tasks", "Tasks"], ["performance", "Performance"], ["book", "Book"]];
    return `<nav class="diagnostics-tabs" role="tablist" aria-label="Diagnostics views">${tabs.map(([value, label]) => `<button type="button" role="tab" class="${state.diagnosticsTab === value ? "active" : ""}" data-action="diagnostics-tab" data-diagnostics-tab="${value}" aria-selected="${state.diagnosticsTab === value}">${label}</button>`).join("")}</nav>`;
  };

  const diagnosticActiveTasks = () => state.backgroundTasks.filter((task) => ["Queued", "Running", "Cancelling"].includes(valueFor(task, "state", "")));
  const diagnosticFailedTasks = () => state.backgroundTasks.filter((task) => valueFor(task, "state", "") === "Failed");
  const diagnosticEvents = () => valueFor(window, "uiDiagnostics", []);
  const diagnosticPerformanceEvents = () => diagnosticEvents().filter((item) => (Number(valueFor(item, "durationMilliseconds", 0)) || 0) > 0 || (Number(valueFor(item, "severity", 0)) || 0) > 0);
  const diagnosticSlowEvents = () => diagnosticPerformanceEvents().filter((item) => (Number(valueFor(item, "severity", 0)) || 0) > 0);
  const diagnosticWorstDuration = () => diagnosticSlowEvents().reduce((worst, item) => Math.max(worst, Number(valueFor(item, "durationMilliseconds", 0)) || 0), 0);
  const diagnosticMissingFolders = (summary) => valueFor(summary, "sourceFolders", []).filter((folder) => valueFor(folder, "status", "Missing") !== "Present");
  const diagnosticRuntimeHealth = () => {
    const failed = diagnosticFailedTasks().length;
    const active = diagnosticActiveTasks().length;
    if (failed) return { label: "Needs attention", tone: "bad", detail: `${failed} failed task${failed === 1 ? "" : "s"}` };
    if (active) return { label: "Active", tone: "warn", detail: `${active} task${active === 1 ? "" : "s"} running` };
    return { label: "Healthy", tone: "good", detail: "No failed tasks" };
  };
  const diagnosticUiHealth = () => {
    const slow = diagnosticSlowEvents().length;
    return slow ? { label: "Needs review", tone: "warn", detail: `${slow} slow operation${slow === 1 ? "" : "s"}` } : { label: "Healthy", tone: "good", detail: "No slow operations" };
  };
  const diagnosticLatestSlowEvent = () => [...diagnosticSlowEvents()].sort((left, right) => new Date(valueFor(right, "timestamp", 0)).getTime() - new Date(valueFor(left, "timestamp", 0)).getTime())[0] ?? null;
  const diagnosticAttentionItems = (summary) => {
    const items = [];
    const failed = diagnosticFailedTasks();
    if (failed.length) items.push({ tone: "bad", title: `${failed.length} failed background task${failed.length === 1 ? "" : "s"}`, detail: "Open Tasks for details." });
    const slow = diagnosticSlowEvents();
    if (slow.length) items.push({ tone: "warn", title: `${slow.length} slow UI operation${slow.length === 1 ? "" : "s"}`, detail: `Worst ${diagnosticWorstDuration()} ms.` });
    if (summary) {
      const workspace = workspaceStatus(summary);
      if (["Failed", "Interrupted", "Cancelled"].includes(workspace)) items.push({ tone: "bad", title: `Selected Book is ${workspace}`, detail: "Open Book diagnostics for workspace details." });
      const missing = diagnosticMissingFolders(summary);
      if (missing.length) items.push({ tone: "warn", title: `${missing.length} source folder${missing.length === 1 ? "" : "s"} unavailable`, detail: missing.map((folder) => valueFor(folder, "name", "Unknown folder")).join(", ") });
    }
    return items;
  };
  const diagnosticRecentTasks = () => [...state.backgroundTasks].sort((left, right) => new Date(valueFor(right, "finishedAt", null) ?? valueFor(right, "startedAt", 0)).getTime() - new Date(valueFor(left, "finishedAt", null) ?? valueFor(left, "startedAt", 0)).getTime()).slice(0, 5);
  const renderDiagnosticHealthCard = (title, value, detail, tone) => `<article class="diagnostic-health-card"><span>${escapeHtml(title)}</span><strong class="diagnostic-health-${tone}">${escapeHtml(value)}</strong><small>${escapeHtml(detail)}</small></article>`;
  const renderDiagnosticsAttention = (items) => `<section class="panel diagnostic-attention"><h2 class="panel-title">Needs attention</h2>${items.length ? `<ul class="diagnostic-attention-list">${items.map((item) => `<li class="diagnostic-attention-${item.tone}"><strong>${escapeHtml(item.title)}</strong><span>${escapeHtml(item.detail)}</span></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No diagnostic issues detected.</p>"}</section>`;
  const renderDiagnosticsRecentActivity = (tasks) => `<section class="panel diagnostic-recent"><h2 class="panel-title">Recent activity</h2>${tasks.length ? `<ul class="diagnostic-recent-list">${tasks.map((task) => `<li><strong>${escapeHtml(valueFor(task, "kind", "Task"))}</strong>${badge(valueFor(task, "state", "Unknown"))}<span>${escapeHtml(valueFor(task, "subject", "—"))}</span><time>${dateTime(valueFor(task, "finishedAt", null) ?? valueFor(task, "startedAt", null))}</time></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No retained background activity.</p>"}</section>`;
  const renderDiagnosticsSummary = (book, summary) => {
    const runtime = diagnosticRuntimeHealth();
    const ui = diagnosticUiHealth();
    const missing = summary ? diagnosticMissingFolders(summary) : [];
    const bookName = book ? valueFor(book, "name", bookId(book)) : "No Book selected";
    const bookState = summary ? workspaceStatus(summary) : "Not selected";
    const bookTone = ["Failed", "Interrupted", "Cancelled"].includes(bookState) ? "bad" : bookState === "Running" ? "warn" : "good";
    return `<section role="tabpanel" data-diagnostics-panel="summary"><div class="diagnostic-health-grid">${renderDiagnosticHealthCard("Runtime", runtime.label, runtime.detail, runtime.tone)}${renderDiagnosticHealthCard("UI health", ui.label, ui.detail, ui.tone)}${renderDiagnosticHealthCard("Selected Book", bookState, bookName, bookTone)}${renderDiagnosticHealthCard("Source files", summary ? (missing.length ? `${missing.length} missing` : "All present") : "No Book selected", summary ? `${valueFor(summary, "sourceFolders", []).length} tracked folders` : "", missing.length ? "warn" : "good")}</div>${renderDiagnosticsAttention(diagnosticAttentionItems(summary))}${renderDiagnosticsRecentActivity(diagnosticRecentTasks())}</section>`;
  };
  const diagnosticTaskRows = () => state.backgroundTasks.slice(0, 20).map((task) => `<tr><td>${escapeHtml(valueFor(task, "kind", ""))}</td><td>${badge(valueFor(task, "state", "Unknown"))}</td><td>${escapeHtml(valueFor(task, "subject", "—"))}</td><td>${escapeHtml(valueFor(task, "step", "—"))}</td><td>${valueFor(task, "completed", "—")}/${valueFor(task, "total", "—")}</td><td>${dateTime(valueFor(task, "startedAt", null))}</td><td>${dateTime(valueFor(task, "finishedAt", null))}</td><td>${escapeHtml(valueFor(task, "errorMessage", "—"))}</td></tr>`).join("") || "<tr><td colspan=\"8\" class=\"empty-row\">No retained background tasks.</td></tr>";
  const renderDiagnosticsTasks = () => `<section role="tabpanel" data-diagnostics-panel="tasks"><div class="diagnostic-detail-strip"><div><span>Active</span><strong>${diagnosticActiveTasks().length}</strong></div><div><span>Failed</span><strong>${diagnosticFailedTasks().length}</strong></div><div><span>Retained</span><strong>${state.backgroundTasks.length}</strong></div></div>${panel("Background workers", `<div class="table-scroll"><table class="data-table"><thead><tr><th>Kind</th><th>State</th><th>Subject</th><th>Step</th><th>Progress</th><th>Started</th><th>Finished</th><th>Error</th></tr></thead><tbody>${diagnosticTaskRows()}</tbody></table></div>`)}</section>`;
  const diagnosticPerformanceRows = () => {
    const events = diagnosticPerformanceEvents();
    if (!events.length) return "<tr><td colspan=\"7\" class=\"empty-row\">No meaningful UI performance operations recorded.</td></tr>";
    return events.map((item) => `<tr><td>${dateTime(valueFor(item, "timestamp", null))}</td><td>${escapeHtml(valueFor(item, "severity", "Info"))}</td><td>${escapeHtml(valueFor(item, "kind", "operation"))}</td><td>${escapeHtml(valueFor(item, "operation", ""))}</td><td>${Number(valueFor(item, "durationMilliseconds", 0)) || 0} ms</td><td>${escapeHtml(valueFor(item, "subject", "—") || "—")}</td><td>${escapeHtml(valueFor(item, "activeOperations", []).join(", ") || "—")}</td></tr>`).join("");
  };
  const renderDiagnosticsPerformance = () => {
    const slow = diagnosticSlowEvents();
    const worst = diagnosticWorstDuration();
    const latest = diagnosticLatestSlowEvent();
    return `<section role="tabpanel" data-diagnostics-panel="performance"><div class="diagnostic-detail-strip"><div><span>Slow operations</span><strong>${slow.length}</strong></div><div><span>Worst duration</span><strong>${worst ? `${worst} ms` : "—"}</strong></div><div><span>Latest slow operation</span><strong>${escapeHtml(latest ? valueFor(latest, "operation", "—") : "—")}</strong></div></div>${panel("UI responsiveness", `<div class="table-scroll"><table class="data-table"><thead><tr><th>Time</th><th>Severity</th><th>Kind</th><th>Operation</th><th>Duration</th><th>Subject</th><th>Active during stall</th></tr></thead><tbody>${diagnosticPerformanceRows()}</tbody></table></div>`)}</section>`;
  };
  const diagnosticLogText = (log) => [String(valueFor(log, "eventName", "") ?? "").trim(), String(valueFor(log, "detail", "") ?? "").trim()].filter(Boolean).join(" · ");
  const diagnosticMeaningfulLogs = (summary) => valueFor(summary, "logs", []).filter((log) => { const text = diagnosticLogText(log); return text && text !== "."; }).slice(-12).reverse();
  const renderDiagnosticsBook = (book, summary) => {
    const selectedId = state.selectedBookId || bookId(book);
    const selector = `<label class="field diagnostic-book-field"><span>Book</span><select class="control diagnostic-select" data-action="diagnostic-book">${books().map((item) => `<option value="${escapeHtml(bookId(item))}" ${bookId(item) === selectedId ? "selected" : ""}>${escapeHtml(valueFor(item, "name", bookId(item)))}</option>`).join("")}</select></label>`;
    if (!book || !summary) return `<section role="tabpanel" data-diagnostics-panel="book"><div class="diagnostic-book-toolbar">${selector}</div>${panel("Book diagnostics", "<p class=\"empty-copy\">Select a Book to inspect its workspace.</p>")}</section>`;
    const folders = valueFor(summary, "sourceFolders", []);
    const logs = diagnosticMeaningfulLogs(summary);
    return `<section role="tabpanel" data-diagnostics-panel="book"><div class="diagnostic-book-toolbar"><div><strong>${escapeHtml(valueFor(book, "name", bookId(book)))}</strong><span>Workspace and source diagnostics</span></div>${selector}</div><div class="diagnostics-grid">${panel("Workspace", `<dl class="path-grid"><div><dt>Workspace state</dt><dd>${badge(workspaceStatus(summary))}</dd></div><div><dt>Current step</dt><dd>${escapeHtml(valueFor(summary, "currentStep", null) || "Not started")}</dd></div><div><dt>Last run</dt><dd>${dateTime(valueFor(summary, "lastRunAt", null))}</dd></div></dl>`)}${panel("Source folders", `<div class="table-scroll"><table class="data-table"><thead><tr><th>Folder</th><th>Status</th><th>Images</th></tr></thead><tbody>${folders.length ? folders.map((folder) => `<tr><td>${escapeHtml(valueFor(folder, "name", ""))}</td><td>${badge(valueFor(folder, "status", "Missing"))}</td><td>${valueFor(folder, "imageCount", 0)}</td></tr>`).join("") : "<tr><td colspan=\"3\" class=\"empty-row\">No source folders recorded.</td></tr>"}</tbody></table></div>`)}</div>${panel("Recent logs", logs.length ? `<ul class="log-list diagnostic-log-list">${logs.map((log) => `<li><time>${dateTime(valueFor(log, "timestamp", null))}</time><span>${escapeHtml(diagnosticLogText(log))}</span></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No meaningful Book logs recorded.</p>", "mt-5")}</section>`;
  };
  const renderDiagnosticsPanel = (book, summary) => {
    if (state.diagnosticsTab === "tasks") return renderDiagnosticsTasks();
    if (state.diagnosticsTab === "performance") return renderDiagnosticsPerformance();
    if (state.diagnosticsTab === "book") return renderDiagnosticsBook(book, summary);
    return renderDiagnosticsSummary(book, summary);
  };
  const renderDiagnostics = () => {
    const book = selectedBook() ?? books()[0] ?? null;
    const summary = book ? summaryFor(book) : null;
    content.innerHTML = `<div class="page-header"><div><h1>Diagnostics</h1><p>Inspect application health and Book workspace details.</p></div><div class="page-actions"><button class="button-secondary" data-action="refresh-diagnostics">Refresh diagnostics</button></div></div>${renderDiagnosticsTabs()}${renderDiagnosticsPanel(book, summary)}`;
  };

  const render = (route, requestProcess = true) => {
    updateGlobalRefreshControl();
    document.querySelectorAll("[data-route]").forEach((button) => button.classList.toggle("nav-item-active", button.dataset.route === route));
    const subtitle = document.getElementById("page-subtitle");
    if (subtitle) subtitle.textContent = `${routeNames[route] ?? "Application"} workspace`;
    if (!window.appSnapshot) {
      content.innerHTML = state.applicationLoadState === "failed"
        ? renderLoadFailure()
        : panel("Loading library…", "<p class=\"panel-note\">Discovering Books, workspace state and local outputs.</p>");
      return;
    }
    if (route === "configuration") renderConfiguration();
    if (route === "brands") renderBrands();
    if (route === "books") renderBooks();
    if (route === "process") renderProcess(requestProcess);
    if (route === "outputs") renderOutputs();
    if (route === "diagnostics") renderDiagnostics();
    if (state.applicationLoadState === "failed") content.insertAdjacentHTML("afterbegin", renderRefreshFailure());
  };

  document.querySelectorAll("[data-route]").forEach((button) => button.addEventListener("click", () => { render(button.dataset.route); if (button.dataset.route === "diagnostics") { send("diagnostics.get"); send("task.list"); } }));
  const closeBookDrawer = () => {
    if (state.bookInteriorSavePending) return;
    if (hasInteriorDraft(state.selectedBookId) && !window.confirm("Discard unsaved Interior changes?")) return;
    clearInteriorDraft(state.selectedBookId);
    state.bookDrawerScrollTop = 0;
    state.bookDrawerOpen = false;
    state.restoreBookFocus = true;
    render("books", false);
  };
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape" || !state.bookDrawerOpen) return;
    event.preventDefault();
    closeBookDrawer();
  });
  content.addEventListener("click", (event) => {
    const target = event.target.closest("[data-action]");
    if (!target) return;
    const action = target.dataset.action;
    if (action === "diagnostics-tab") {
      state.diagnosticsTab = diagnosticsTabValue(target.dataset.diagnosticsTab);
      render("diagnostics", false);
      return;
    }
    if (action === "refresh" || action === "validate-all") beginApplicationRefresh();
    if (action === "refresh-diagnostics") { send("diagnostics.get"); send("task.list"); }
    if (action === "save-settings") { const payload = {}; document.querySelectorAll("[data-setting]").forEach((input) => { const group = input.dataset.settingGroup; if (group) { payload[group] ??= {}; payload[group][input.dataset.setting] = Number(input.value); } else payload[input.dataset.setting] = Number(input.value); }); send("settings.save", payload); }
    if (action === "select-brand") { state.selectedBrand = target.dataset.brandName; if (brandSelect) brandSelect.value = state.selectedBrand; render("brands"); }
    if (action === "load-brand-settings") send("brand.settings.get", { brandName: state.selectedBrand });
    if (action === "save-brand-settings") send("brand.settings.save", { brandName: state.selectedBrand, json: state.brandSettings });
    if (action === "select-book") { state.selectedBookId = target.dataset.bookId; state.selectedBookTab = "overview"; state.selectedAssetReference = ""; state.bookDrawerScrollTop = 0; state.bookDrawerOpen = true; state.drawerFocusTitle = true; render("books", false); }
    if (action === "close-book-drawer") closeBookDrawer();
    if (action === "save-book-interior-settings" && !state.bookInteriorSavePending) {
      const payload = interiorSavePayload(target.dataset.bookId);
      if (payload) {
        state.bookInteriorSavePending = true;
        updateInteriorSaveUi();
        send("book.interior.settings.save", payload);
      }
    }
    if (action === "intro-add-template" || action === "intro-remove-template" || action === "intro-move-template") {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      if (book) {
        const summary = summaryFor(book);
        const current = effectiveIntro(book, summary);
        let sourceReferences = [...current.sourceReferences];
        if (action === "intro-add-template") sourceReferences.push(target.dataset.introSourceReference);
        if (action === "intro-remove-template") sourceReferences = sourceReferences.filter((reference) => reference.toLowerCase() !== target.dataset.introSourceReference.toLowerCase());
        if (action === "intro-move-template") {
          const index = Number(target.dataset.introIndex);
          const next = target.dataset.introDirection === "up" ? index - 1 : index + 1;
          if (Number.isInteger(index) && next >= 0 && next < sourceReferences.length) [sourceReferences[index], sourceReferences[next]] = [sourceReferences[next], sourceReferences[index]];
        }
        stageIntroChange(book, summary, true, sourceReferences);
        status.textContent = "Unsaved Intro and Interior changes";
        render("books", false);
      }
    }
    if (action === "queue-book") { const id = target.dataset.bookId; if (target.checked) state.selectedBookIds.add(id); else state.selectedBookIds.delete(id); }
    if (action === "queue-selected-book") {
      const book = books().find((item) => bookId(item) === state.selectedBookId);
      const readiness = book ? processingReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.selectedBookIds.add(state.selectedBookId);
      render("process");
    }
    if (action === "book-tab") { state.selectedBookTab = target.dataset.bookTab; render("books", false); }
    if (action === "select-asset") { state.selectedAssetReference = target.dataset.sourceReference; render("books", false); }
    if (action === "asset-view") { state.assetView = target.dataset.assetView; render("books", false); }
    if (action === "asset-folder") { state.assetFolder = target.dataset.assetFolder; render("books", false); }
    if (action === "book-status") { state.bookStatus = target.dataset.bookStatus; state.bookPage = 1; render("books", false); }
    if (action === "book-view") { state.bookView = target.dataset.bookView; render("books", false); }
    if (action === "clear-book-filters") { state.bookFilter = ""; state.bookStatus = "All"; state.bookPage = 1; render("books", false); }
    if (action === "clear-cache" && !cacheCleanupBlocked()) {
      if (window.confirm("Clear processed image cache for completed Books?")) {
        state.cacheCleanupResultRequested = false;
        send("cache.clear");
      }
    }
    if (action === "book-page") { const last = Number(target.closest("[data-book-total-pages]")?.dataset.bookTotalPages ?? 1); state.bookPage = target.dataset.bookPage === "first" ? 1 : target.dataset.bookPage === "last" ? last : Math.min(last, Math.max(1, state.bookPage + (target.dataset.bookPage === "next" ? 1 : -1))); render("books", false); }
    if (action === "pdf-library-page") { const totalPages = Math.max(1, Math.ceil(pdfLibraryBooks().length / pdfLibraryPageSize)); state.pdfLibraryPage = target.dataset.pdfLibraryPage === "first" ? 1 : target.dataset.pdfLibraryPage === "last" ? totalPages : Math.min(totalPages, Math.max(1, state.pdfLibraryPage + (target.dataset.pdfLibraryPage === "next" ? 1 : -1))); render("outputs", false); }
    if (action === "pdf-library-view") { state.pdfLibraryView = target.dataset.pdfLibraryView === "list" ? "list" : "grid"; render("outputs", false); }
    if (action === "validate-book") send("book.validate", { bookId: target.dataset.bookId });
    if (action === "go-process") render("process");
    if (action === "start-process" && !state.processStartPending) {
      const blocked = [...state.selectedBookIds].map((id) => books().find((book) => bookId(book) === id)).filter(Boolean).map((book) => ({ book, readiness: processingReadiness(book, summaryFor(book)) })).find((item) => !item.readiness.ready);
      if (blocked) { status.textContent = `${valueFor(blocked.book, "name", "Book")}: ${blocked.readiness.reason}`; return; }
      state.processStartPending = true;
      send("process.start", { bookIds: [...state.selectedBookIds], brandName: state.selectedBrand || brandSelect?.value || null, mode: "interior-only" });
    }
    if (action === "cancel-process") send("process.cancel");
    if (action === "open-output") send("book.output.open", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
    if (action === "reveal-output") send("book.output.reveal", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
    if (action === "copy-output-path") send("book.output.copy-path", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
  });
  content.addEventListener("input", (event) => {
    if (event.target.dataset.action === "filter-books") { state.bookFilter = event.target.value; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "filter-assets") { state.assetFilter = event.target.value; render("books", false); }
    if (event.target.dataset.action === "pdf-library-search") { state.pdfLibrarySearch = event.target.value; state.pdfLibraryPage = 1; state.pdfLibrarySearchFocused = true; state.pdfLibrarySearchCaret = event.target.selectionStart ?? event.target.value.length; render("outputs", false); }
    if (event.target.dataset.brandSettings !== undefined) state.brandSettings = event.target.value;
  });
  content.addEventListener("change", (event) => {
    if (event.target.dataset.action === "diagnostic-book") { state.selectedBookId = event.target.value; render("diagnostics", false); }
    if (event.target.dataset.action === "set-book-background") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      if (book) { stageBackgroundChange(book, summaryFor(book), event.target.checked); status.textContent = "Unsaved Interior changes"; updateInteriorSaveUi(); }
    }
    if (event.target.dataset.action === "set-intro-mode") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      if (book) {
        const summary = summaryFor(book);
        const current = effectiveIntro(book, summary);
        stageIntroChange(book, summary, event.target.value === "custom", current.sourceReferences);
        status.textContent = "Unsaved Intro and Interior changes";
        render("books", false);
      }
    }
    if (event.target.dataset.action === "set-interior-active" || event.target.dataset.action === "set-interior-frame-mode") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      const asset = book ? assetForReference(summaryFor(book), event.target.dataset.sourceReference) : null;
      if (book && asset) {
        const field = event.target.dataset.action === "set-interior-active" ? "active" : "frameMode";
        stageInteriorAssetChange(book, asset, field, event.target.dataset.action === "set-interior-active" ? event.target.checked : event.target.value);
        status.textContent = "Unsaved Interior changes";
        updateInteriorSaveUi();
      }
    }
    if (event.target.dataset.action === "book-sort") { state.bookSort = event.target.value; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "pdf-library-sort") { state.pdfLibrarySort = ["newest", "name", "size"].includes(event.target.value) ? event.target.value : "newest"; state.pdfLibraryPage = 1; render("outputs", false); }
  });
  content.addEventListener("error", (event) => {
    const image = event.target;
    if (!image?.matches?.("img[data-local-image]")) return;
    if (image.dataset.introTemplateId) {
      state.introTemplateDimensions.set(image.dataset.introTemplateId, { valid: false });
    }
    const fallback = document.createElement("span");
    fallback.className = "book-preview-fallback";
    fallback.setAttribute("aria-label", image.dataset.imageFallback || "Image unavailable");
    fallback.textContent = image.dataset.imageFallback || "Image unavailable";
    image.replaceWith(fallback);
    if (image.dataset.introTemplateId && state.bookDrawerOpen && state.selectedBookTab === "assets") render("books", false);
  }, true);
  content.addEventListener("load", (event) => {
    const image = event.target;
    if (!image?.matches?.("img[data-local-image]") || !image.dataset.introTemplateId) return;
    const valid = isSupportedIntroTemplateSize(image.naturalWidth, image.naturalHeight);
    const previous = state.introTemplateDimensions.get(image.dataset.introTemplateId);
    if (previous?.valid === valid && previous.width === image.naturalWidth && previous.height === image.naturalHeight) return;
    state.introTemplateDimensions.set(image.dataset.introTemplateId, { valid, width: image.naturalWidth, height: image.naturalHeight });
    if (state.bookDrawerOpen && state.selectedBookTab === "assets") render("books", false);
  }, true);
  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const responseId = valueFor(response, "id", "");
    const requestCommand = state.pendingCommands.get(responseId) ?? "";
    state.pendingCommands.delete(responseId);
    const ok = valueFor(response, "ok", false);
    const command = valueFor(response, "command", "");
    if (ok && command === "app.pong") {
      status.textContent = "Connected";
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "LibraryRefresh") {
      if (requestCommand === "book.interior.settings.save") {
        state.bookInteriorSavePending = false;
        clearInteriorDraft(state.selectedBookId);
        status.textContent = "Interior changes saved";
        updateInteriorSaveUi();
      }
      observeLibraryRefresh(valueFor(response, "payload", {}));
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "CacheCleanup") {
      observeCacheCleanup(valueFor(response, "payload", {}));
    } else if (ok && command === "app.snapshot") {
      window.appSnapshot = valueFor(response, "payload", {});
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      state.libraryRefreshTaskId = "";
      state.libraryRefreshResultRequested = false;
      const allBrands = valueFor(discovery(), "brands", []);
      if (!state.selectedBrand && allBrands.length) state.selectedBrand = valueFor(allBrands[0], "name", "");
      if (brandSelect) brandSelect.innerHTML = allBrands.length ? allBrands.map((brand) => `<option>${escapeHtml(valueFor(brand, "name", ""))}</option>`).join("") : "<option>No brands</option>";
      if (brandSelect) brandSelect.value = state.selectedBrand;
      render(document.querySelector(".nav-item-active")?.dataset.route ?? "books", false);
      status.textContent = "Connected";
    } else if (ok && command === "cache.cleanup.result") {
      const result = valueFor(response, "payload", {});
      const cleaned = valueFor(result, "cleanedBooks", 0);
      const skipped = valueFor(result, "skippedBooks", 0);
      const failed = valueFor(result, "failedBooks", 0);
      const freed = fileSize(valueFor(result, "freedBytes", 0));
      state.cacheCleanupTaskId = "";
      state.cacheCleanupResultRequested = false;
      state.cacheCleanupActive = false;
      status.textContent = `Cleared ${cleaned} Books • Freed ${freed}` + (skipped ? ` • ${skipped} skipped` : "") + (failed ? ` • ${failed} failed` : "");
      beginApplicationRefresh();
    } else if (ok && command === "settings.saved") {
      window.appSnapshot = { ...(window.appSnapshot ?? {}), globalSettings: valueFor(response, "payload", {}) };
      render("configuration", false);
      status.textContent = "Settings saved";
    } else if (ok && command === "process.snapshot") {
      window.processSnapshot = valueFor(response, "payload", {});
      state.processStartPending = false;
      const startedAt = valueFor(window.processSnapshot, "startedAt", "");
      const terminal = !valueFor(window.processSnapshot, "isActive", false) && !valueFor(window.processSnapshot, "isCancelling", false);
      if (terminal && startedAt && state.lastTerminalRefreshSession !== startedAt) {
        state.lastTerminalRefreshSession = startedAt;
        beginApplicationRefresh();
      }
      updateGlobalProcessStatus();
      if (document.querySelector(".nav-item-active")?.dataset.route === "process") render("process", false);
      if (document.querySelector(".nav-item-active")?.dataset.route === "books") render("books", false);
      status.textContent = "Connected";
    } else if (ok && command === "brand.settings") {
      state.brandSettings = valueFor(response, "payload", "{}");
      if (document.querySelector(".nav-item-active")?.dataset.route === "brands") render("brands", false);
      status.textContent = "Connected";
    } else if (ok && command === "brand.settings.saved") {
      state.brandSettings = valueFor(response, "payload", "{}");
      if (document.querySelector(".nav-item-active")?.dataset.route === "brands") render("brands", false);
      status.textContent = "Brand settings saved";
      beginApplicationRefresh();
    } else if (ok && command === "book.output.action.completed") {
      status.textContent = "Output action completed";
    } else if (ok && command === "diagnostics.snapshot") {
      window.uiDiagnostics = valueFor(response, "payload", []);
      if (currentRoute() === "diagnostics") render("diagnostics", false);
      status.textContent = "Diagnostics refreshed";
    } else if (ok && command === "background.tasks") {
      state.backgroundTasks = valueFor(response, "payload", []);
      if (currentRoute() === "diagnostics") render("diagnostics", false);
    } else {
      const error = valueFor(response, "error", "unexpected response");
      if (requestCommand === "book.interior.settings.save") {
        state.bookInteriorSavePending = false;
        updateInteriorSaveUi();
      }
      if (requestCommand === "app.refresh") {
        if (error === "cache_cleanup_active") {
          state.applicationLoadState = window.appSnapshot ? "ready" : "idle";
          state.applicationLoadError = "";
          status.textContent = "Clear Cache is running";
          render(currentRoute(), false);
          return;
        }
        state.applicationLoadState = "failed";
        state.applicationLoadError = error;
        render(currentRoute(), false);
      }
      if (requestCommand === "cache.clear" && ["cache_cleanup_processing_active", "cache_cleanup_refresh_active"].includes(error)) {
        state.cacheCleanupTaskId = "";
        state.cacheCleanupResultRequested = false;
        state.cacheCleanupActive = false;
        status.textContent = error === "cache_cleanup_processing_active" ? "Interior Processing is running" : "Library refresh is running";
        if (currentRoute() === "books") render("books", false);
        return;
      }
      if (["book.background.set", "book.interior.active.set", "book.interior.settings.save"].includes(requestCommand) && error === "processing_active") {
        status.textContent = "Interior Processing is running";
        updateInteriorSaveUi();
        if (currentRoute() === "books") render("books", false);
        return;
      }
      if (requestCommand === "process.start") {
        state.processStartPending = false;
        if (error === "cache_cleanup_active") {
          status.textContent = "Clear Cache is running";
          return;
        }
      }
      status.textContent = `Bridge error: ${error}`;
    }
  });

  const refreshButton = document.getElementById("refresh-button");
  if (refreshButton) refreshButton.addEventListener("click", beginApplicationRefresh);
  const globalProcessStatus = document.getElementById("global-process-status");
  if (globalProcessStatus) globalProcessStatus.addEventListener("click", () => render("process"));
  updateGlobalProcessStatus();
  if (brandSelect) brandSelect.addEventListener("change", () => {
    state.selectedBrand = brandSelect.value;
    render(document.querySelector(".nav-item-active")?.dataset.route ?? "books", false);
  });
  window.setInterval(() => { if (valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false)) send("process.get"); }, 1000);
  send("app.ping");
  state.applicationLoadState = "loading";
  render("books", false);
  send("app.refresh");
})();
