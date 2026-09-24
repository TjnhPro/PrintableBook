(() => {
  const status = document.getElementById("bridge-status");
  const content = document.getElementById("app-content");
  const routeNames = { configuration: "Settings", brands: "Brands & templates", books: "Book Library", process: "Interior processing", outputs: "PDF Library", diagnostics: "Diagnostics" };
  const bookStatuses = ["All", "Needs review", "Ready", "Processing", "PDF ready", "Failed"];
  const state = { inspectedBrand: "", selectedBookId: "", selectedBookIds: new Set(), selectedBookTab: "overview", bookDrawerOpen: false, drawerFocusTitle: false, restoreBookFocus: false, bookDrawerScrollTop: 0, artworkGridScrollTop: 0, bookListRefreshPending: false, selectedArtworkReferences: new Set(), assetBulkActive: "unchanged", assetBulkFrameMode: "unchanged", bookInteriorDrafts: new Map(), bookMetadataDrafts: new Map(), subcoverTouchedBooks: new Set(), brandAuthorDrafts: new Map(), catalogMutationPending: false, catalogMutationAwaitingSnapshot: false, catalogMutationCommand: "", catalogMutationTarget: "", catalogFeedback: "", catalogFeedbackError: false, introTemplateDimensions: new Map(), introTemplatePage: 1, bookInteriorSavePending: false, bookInteriorSaveTaskId: "", bookInteriorSaveAwaitingSnapshot: false, brandTemplateCopyPending: false, productionImportPending: "", productionActionTaskId: "", productionActionPollTimer: null, productionActionName: "", productionFeedback: "", productionFeedbackError: false, productionRefreshAwaitingSnapshot: false, productionFocusSelector: "", productionFinalBuildActive: false, bookFilter: "", bookBrandFilter: "All", bookStatus: "All", bookPage: 1, bookView: "grid", bookSort: "activity", brandFilter: "", brandValidationResult: null, brandValidationRequestBrands: new Map(), selectedAssetReference: "", assetView: "grid", assetFilter: "", assetStatus: "Active", assetFrameMode: "", assetSearchFocused: false, assetSearchCaret: 0, pdfLibrarySearch: "", pdfLibrarySort: "newest", pdfLibraryPage: 1, pdfLibraryView: "grid", pdfLibrarySearchFocused: false, pdfLibrarySearchCaret: 0, pdfLibraryFeedback: "", pdfLibraryFeedbackError: false, pdfLibraryPendingActions: new Set(), pdfLibraryRequestActions: new Map(), applicationLoadState: "idle", applicationLoadError: "", libraryRefreshTaskId: "", libraryRefreshPollTimer: null, libraryRefreshResultRequested: false, cacheCleanupTaskId: "", cacheCleanupPollTimer: null, cacheCleanupResultRequested: false, cacheCleanupActive: false, processTab: "overview", processQueuePage: 1, processStartPending: false, lastTerminalRefreshSession: "", diagnosticsTab: "summary", backgroundTasks: [], pendingCommands: new Map(), updateSnapshot: null, updateCommandPending: "", updatePollTimer: null, updateDismissedVersion: "", updateDialogPreviousFocus: null };

  const escapeHtml = (value) => String(value ?? "").replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" }[character]));
  const valueFor = (object, name, fallback = null) => object?.[name] ?? object?.[name[0].toUpperCase() + name.slice(1)] ?? fallback;
  const discovery = () => valueFor(window.appSnapshot, "discovery", {});
  const books = () => valueFor(discovery(), "books", []);
  const brands = () => valueFor(discovery(), "brands", []);
  const summaries = () => valueFor(window.appSnapshot, "bookSummaries", []);
  const bookId = (book) => valueFor(valueFor(book, "id", {}), "value", valueFor(book, "name", ""));
  const summaryFor = (book) => summaries().find((summary) => valueFor(valueFor(summary, "bookId", {}), "value", "") === bookId(book));
  const pdfLibraryBookName = (book, summary) => bookId(book) || valueFor(valueFor(summary, "bookId", {}), "value", "");
  const pdfLibraryPageSize = 12;
  const processQueuePageSize = 12;
  const pdfLibraryOutputs = (summary) => {
    const newestByKind = new Map();
    for (const output of valueFor(summary, "outputSummaries", [])) {
      const kind = String(valueFor(output, "artifactKind", "Unknown"));
      if (kind !== "Cover" && kind !== "Interior") continue;
      const existing = newestByKind.get(kind);
      const generatedAt = new Date(valueFor(output, "generatedAt", 0)).getTime() || 0;
      const existingGeneratedAt = existing ? new Date(valueFor(existing, "generatedAt", 0)).getTime() || 0 : -1;
      const artifact = String(valueFor(output, "artifactReference", ""));
      const existingArtifact = String(valueFor(existing, "artifactReference", ""));
      if (!existing || generatedAt > existingGeneratedAt || (generatedAt === existingGeneratedAt && artifact.localeCompare(existingArtifact) > 0)) newestByKind.set(kind, output);
    }
    return ["Cover", "Interior"].map((kind) => newestByKind.get(kind)).filter(Boolean);
  };
  const pdfLibraryOutputSize = (summary) => pdfLibraryOutputs(summary).reduce((total, output) => total + (Number(valueFor(output, "fileSizeBytes", 0)) || 0), 0);
  const pdfLibraryGeneratedAt = (summary) => {
    const outputTimes = pdfLibraryOutputs(summary).map((output) => new Date(valueFor(output, "generatedAt", 0)).getTime()).filter((value) => Number.isFinite(value) && value > 0);
    if (outputTimes.length) return Math.max(...outputTimes);
    const lastRun = new Date(valueFor(summary, "lastRunAt", 0)).getTime();
    return Number.isFinite(lastRun) ? lastRun : 0;
  };
  const eligiblePdfLibraryBooks = () => books().map((book) => ({ book, summary: summaryFor(book) })).filter(({ summary }) => summary && pdfLibraryOutputs(summary).length > 0);
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
  const brandValidationStatus = (value) => typeof value === "number" ? ["Not validated", "Validated", "Needs validation"][value] ?? "Not validated" : String(value ?? "NotValidated").replace(/([a-z])([A-Z])/g, "$1 $2");
  const brandSummaries = () => valueFor(window.appSnapshot, "brandSummaries", []);
  const brandSummaryFor = (brand) => brandSummaries().find((summary) => valueFor(summary, "brandName", "") === valueFor(brand, "name", ""));
  const assignmentStatus = (summary) => {
    const value = valueFor(summary, "assignmentStatus", "Unassigned");
    return typeof value === "number" ? ["Unassigned", "Valid", "BookAuthorMissing", "BrandAuthorMissing", "AuthorMismatch", "MissingBrand", "BrandMetadataUnavailable"][value] ?? "Unassigned" : String(value ?? "Unassigned");
  };
  const assignmentLabel = (summary) => ({ BookAuthorMissing: "Book Author missing", BrandAuthorMissing: "Brand Author missing", AuthorMismatch: "Author mismatch", MissingBrand: "Brand missing", BrandMetadataUnavailable: "Brand metadata unavailable" })[assignmentStatus(summary)] ?? assignmentStatus(summary);
  const metadataFor = (summary) => valueFor(summary, "metadata", {}) ?? {};
  const bookDisplayTitle = (book, summary = summaryFor(book)) => String(valueFor(metadataFor(summary), "title", "") || valueFor(book, "name", bookId(book)) || "Unknown");
  const normalizedAuthor = (value) => String(value ?? "").trim().toLowerCase();
  const authorMatches = (left, right) => Boolean(normalizedAuthor(left)) && normalizedAuthor(left) === normalizedAuthor(right);
  const assignedBrandName = (summary) => String(valueFor(summary, "assignedBrand", "") ?? "").trim();
  const assignedBrandFor = (summary) => {
    const assigned = assignedBrandName(summary);
    return assigned ? brands().find((brand) => valueFor(brand, "name", "") === assigned) ?? null : null;
  };
  const brandAuthor = (brand) => String(valueFor(brandSummaryFor(brand), "author", "") ?? "");
  const matchingBrandsFor = (summary) => {
    const author = valueFor(metadataFor(summary), "author", "");
    return brands().filter((brand) => authorMatches(author, brandAuthor(brand)));
  };
  const metadataValues = (summary) => {
    const metadata = metadataFor(summary);
    return { title: String(valueFor(metadata, "title", "") ?? ""), subtitle: String(valueFor(metadata, "subtitle", "") ?? ""), subcover: String(valueFor(metadata, "subcover", "") ?? ""), description: String(valueFor(metadata, "description", "") ?? ""), author: String(valueFor(metadata, "author", "") ?? "") };
  };
  const metadataDraftFor = (book, summary, create = false) => {
    const id = bookId(book);
    let draft = state.bookMetadataDrafts.get(id);
    if (!draft && create) { draft = { ...metadataValues(summary) }; state.bookMetadataDrafts.set(id, draft); }
    return draft ?? metadataValues(summary);
  };
  const hasMetadataDraft = (book, summary) => {
    const draft = state.bookMetadataDrafts.get(bookId(book));
    if (!draft) return false;
    const persisted = metadataValues(summary);
    return Object.keys(persisted).some((key) => draft[key] !== persisted[key]);
  };
  const subcoverError = (value) => {
    const trimmed = String(value ?? "").trim();
    const length = trimmed && typeof Intl?.Segmenter === "function" ? [...new Intl.Segmenter(undefined, { granularity: "grapheme" }).segment(trimmed)].length : [...trimmed].length;
    return length >= 100 ? "Subcover must contain fewer than 100 characters." : "";
  };
  const brandAuthorDraftFor = (brand) => state.brandAuthorDrafts.get(valueFor(brand, "name", "")) ?? brandAuthor(brand);
  const brandAuthorIsDirty = (brand, value) => value !== brandAuthor(brand) || valueFor(brandSummaryFor(brand), "metadataStatus", "Missing") === "Unavailable";
  const catalogMutationBusy = () => state.catalogMutationPending || state.catalogMutationAwaitingSnapshot;
  const metadataAssignmentWarning = (draft, summary) => {
    const assigned = assignedBrandName(summary);
    if (!assigned) return "";
    const brand = brands().find((item) => valueFor(item, "name", "") === assigned);
    return brand && authorMatches(draft.author, brandAuthor(brand)) ? "" : `Saving this Author will make the '${assigned}' assignment invalid. The assignment will be kept for review.`;
  };
  const isValidatedBrand = (brand) => brandValidationStatus(valueFor(brandSummaryFor(brand), "validationStatus", "NotValidated")) === "Validated";
  const frameModeValue = (value) => {
    if (typeof value === "number") return ["disabled", "enabled"][value] ?? "disabled";
    const normalized = String(value ?? "disabled").toLowerCase();
    return ["enabled", "disabled"].includes(normalized) ? normalized : "disabled";
  };
  const workspaceStatus = (summary) => displayStatus(valueFor(summary, "workspaceStatus", "Not started"));
  const workspaceStateAvailable = (summary) => valueFor(summary, "workspaceStateAvailable", true) !== false;
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
    const modes = valueFor(summary, "interiorSourcePages", []).map((source) => frameModeValue(valueFor(source, "frameMode", "disabled")));
    if (!modes.length) return "Needs review";
    if (modes.every((mode) => mode === "enabled")) return "Frame";
    if (modes.every((mode) => mode === "disabled")) return "No Frame";
    return "Mixed";
  };
  const statusClass = (value) => value === "Ready" || value === "Completed" || value === "Present" || value === "Validated" || value === "Processed" || value === "Production" ? "status-good" : value === "Invalid" || value === "Failed" || value === "Unreadable" || value === "Needs attention" || value === "Missing" || value === "Author mismatch" || value === "Book Author missing" || value === "Brand Author missing" || value === "Brand missing" || value === "Brand metadata unavailable" ? "status-bad" : value === "Needs selection" || value === "Needs validation" || value === "Running" || value === "Processing" || value === "Stale" || value === "Ready to process" ? "status-warn" : "status-muted";
  const badge = (value) => { const label = displayStatus(value); return `<span class="status-badge ${statusClass(label)}">${escapeHtml(label)}</span>`; };
  const bookSelectIcon = (selected) => `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="3.5" y="3.5" width="17" height="17" rx="4"></rect>${selected ? '<path d="m7.5 12.5 3 3 6-7"></path>' : ""}</svg>`;
  const bookEditIcon = () => '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="m4 20 4.1-1 10-10a2.1 2.1 0 0 0-3-3l-10 10L4 20Z"></path><path d="m13.7 7.3 3 3"></path></svg>';
  const send = (command, payload) => {
    const id = crypto.randomUUID();
    state.pendingCommands.set(id, command);
    window.chrome.webview.postMessage(JSON.stringify({ version: 1, id, command, ...(payload ? { payload } : {}) }));
    return id;
  };
  const setPdfLibraryFeedback = (message, isError = false) => {
    state.pdfLibraryFeedback = message;
    state.pdfLibraryFeedbackError = isError;
    const feedback = content.querySelector?.("[data-pdf-library-feedback]");
    if (!feedback) return;
    feedback.hidden = !message;
    feedback.textContent = message;
    feedback.classList?.toggle("is-error", isError);
    feedback.setAttribute?.("role", isError ? "alert" : "status");
  };
  const setPdfLibraryActionBusy = (target, busy) => {
    if (!target) return;
    target.disabled = busy;
    target.setAttribute?.("aria-busy", String(busy));
    const label = target.querySelector?.("[data-output-action-label]");
    if (label) label.textContent = busy ? valueFor(target.dataset, "outputBusyLabel", "Opening…") : valueFor(target.dataset, "outputIdleLabel", "Open");
  };
  const beginPdfLibraryAction = (command, payload, key, target) => {
    if (state.pdfLibraryPendingActions.has(key)) return;
    state.pdfLibraryPendingActions.add(key);
    const restoreFocus = document.activeElement === target;
    setPdfLibraryActionBusy(target, true);
    setPdfLibraryFeedback("Opening…");
    try {
      const requestId = send(command, payload);
      state.pdfLibraryRequestActions.set(requestId, { key, target, restoreFocus });
    } catch {
      state.pdfLibraryPendingActions.delete(key);
      setPdfLibraryActionBusy(target, false);
      setPdfLibraryFeedback("The output action could not be sent. Try again.", true);
    }
  };
  const finishPdfLibraryAction = (responseId) => {
    const action = state.pdfLibraryRequestActions.get(responseId);
    if (!action) return null;
    state.pdfLibraryRequestActions.delete(responseId);
    state.pdfLibraryPendingActions.delete(action.key);
    const selector = `[data-output-action-key="${CSS.escape(action.key)}"]`;
    const currentTarget = content.querySelector?.(selector);
    setPdfLibraryActionBusy(action.target, false);
    if (currentTarget && currentTarget !== action.target) setPdfLibraryActionBusy(currentTarget, false);
    const focusTarget = currentTarget ?? action.target;
    if (action.restoreFocus && focusTarget?.focus && (!document.activeElement || document.activeElement === document.body)) focusTarget.focus();
    return action;
  };
  const pdfLibraryActionError = (code) => ({
    invalid_output_action: "The output action was invalid. Refresh PDF Library and try again.",
    output_not_found: "This PDF was moved or deleted. Refresh PDF Library or rebuild the output.",
    output_not_previewable: "This PDF is invalid and cannot be previewed. Open its folder to inspect or rebuild it.",
    output_folder_not_found: "The Book output folder is unavailable. Refresh PDF Library or rebuild an output.",
    output_folder_inconsistent: "Cover and Interior are not in the expected Book output folder. Rebuild the outputs before opening the folder.",
    output_launch_failed: "Windows could not open this output. Check the file association or folder permissions and try again."
  })[code] ?? "The output action failed. Refresh PDF Library and try again.";
  const updatePhase = () => valueFor(state.updateSnapshot, "phase", "Idle");
  const updateIsBusy = () => ["Checking", "Downloading", "Verifying", "Installing"].includes(updatePhase());
  const stopUpdatePolling = () => { if (state.updatePollTimer !== null && window.clearInterval) window.clearInterval(state.updatePollTimer); state.updatePollTimer = null; };
  const startUpdatePolling = () => { if (state.updatePollTimer === null) state.updatePollTimer = window.setInterval(() => send("updates.getState"), 250); };
  const updateStageLabel = () => ({
    DownloadingArchive: "Downloading update…",
    DownloadingChecksum: "Downloading checksum…",
    Verifying: "Verifying update…",
    Extracting: "Extracting update…",
    Validating: "Validating update…",
    Ready: "Update ready"
  })[valueFor(state.updateSnapshot, "preparationStage", "")] ?? "";
  const updateVersion = (snapshot = state.updateSnapshot) => String(valueFor(snapshot, "latestVersion", valueFor(snapshot, "currentVersion", "")));
  const formatBytes = (value) => {
    const bytes = Math.max(0, Number(value) || 0);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${Math.round(bytes / (1024 * 1024) * 10) / 10} MB`;
  };
  const dismissUpdateDialog = () => {
    state.updateDismissedVersion = updateVersion();
    renderUpdateDialog();
  };
  const renderUpdateDialog = () => {
    const root = document.getElementById("update-dialog-root"); if (!root) return;
    const snapshot = state.updateSnapshot; const phase = updatePhase();
    const canDownload = valueFor(snapshot, "canDownload", false); const canInstall = valueFor(snapshot, "canInstall", false);
    const retryAction = canInstall ? "install" : canDownload ? "download" : "";
    const versionKey = updateVersion(snapshot);
    const isDismissed = state.updateDismissedVersion !== "" && state.updateDismissedVersion === versionKey;
    const visible = Boolean(snapshot) && ((phase === "Available" && canDownload && !isDismissed) || ["Downloading", "Verifying", "Installing"].includes(phase) || (phase === "Ready" && canInstall && !isDismissed) || (phase === "Error" && retryAction));
    if (!visible) {
      const wasVisible = !root.hidden;
      root.hidden = true; root.innerHTML = "";
      if (wasVisible && state.updateDialogPreviousFocus?.focus) state.updateDialogPreviousFocus.focus();
      state.updateDialogPreviousFocus = null;
      return;
    }
    const wasHidden = root.hidden;
    if (wasHidden) state.updateDialogPreviousFocus = document.activeElement ?? null;
    const version = escapeHtml(versionKey); const releaseName = escapeHtml(valueFor(snapshot, "releaseName", "")); const notes = escapeHtml(valueFor(snapshot, "releaseNotes", ""));
    const archiveTotal = Number(valueFor(snapshot, "totalBytes", 0));
    const archiveStage = valueFor(snapshot, "preparationStage", "") === "DownloadingArchive";
    const percent = archiveStage && archiveTotal > 0 ? Math.max(0, Math.min(100, Math.round(Number(valueFor(snapshot, "bytesReceived", 0)) / archiveTotal * 100))) : null;
    const progress = percent !== null ? `<div class="update-progress" role="progressbar" aria-label="Download progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${percent}"><span style="width:${percent}%"></span></div><span class="update-progress-copy">${formatBytes(valueFor(snapshot, "bytesReceived", 0))} of ${formatBytes(archiveTotal)} (${percent}%)</span>`
      : ["Downloading", "Verifying"].includes(phase) ? `<div class="update-progress update-progress-indeterminate" aria-hidden="true"><span></span></div>` : "";
    const message = phase === "Available" ? `Printable Book ${version} is available.` : phase === "Ready" ? `Update ${version} is ready.` : phase === "Installing" ? `Restarting to install ${version}…` : phase === "Error" ? escapeHtml(valueFor(snapshot, "errorMessage", "Could not complete the update.")) : escapeHtml(updateStageLabel() || "Verifying update…");
    const title = phase === "Available" ? "Update available" : phase === "Ready" ? "Update ready" : phase === "Error" ? "Update needs attention" : "Preparing update";
    const actions = phase === "Available" ? '<button class="button-secondary" data-update-action="dismiss">Later</button><button class="button-primary" data-update-action="download" data-update-primary>Download update</button>'
      : phase === "Ready" ? '<button class="button-secondary" data-update-action="dismiss">Later</button><button class="button-primary" data-update-action="install" data-update-primary>Restart &amp; Update</button>'
      : phase === "Error" && retryAction ? `<button class="button-primary" data-update-action="${retryAction}" data-update-primary>${retryAction === "install" ? "Restart &amp; Update" : "Retry download"}</button>` : "";
    root.hidden = false;
    root.innerHTML = `<div class="update-dialog-backdrop" aria-hidden="true"></div><section class="update-dialog" role="dialog" aria-modal="true" aria-labelledby="update-dialog-title" aria-describedby="update-dialog-status"><h2 id="update-dialog-title">${title}</h2><div id="update-dialog-status" class="update-dialog-status" aria-live="polite"><strong>${message}</strong>${releaseName ? `<p>${releaseName}</p>` : ""}${progress}${notes ? `<div class="update-release-notes">${notes}</div>` : ""}</div><div class="update-dialog-actions">${actions}</div></section>`;
    if (wasHidden) root.querySelector?.("[data-update-primary]")?.focus?.();
  };
  const updateVersionLabel = () => {
    const versionLabel = document.querySelector(".pb-brand-version");
    const currentVersion = valueFor(state.updateSnapshot, "currentVersion", "");
    if (versionLabel && currentVersion) versionLabel.textContent = `Version ${currentVersion}`;
  };
  const applyUpdateSnapshot = (snapshot) => {
    const previousPhase = updatePhase();
    state.updateSnapshot = snapshot ?? null; state.updateCommandPending = "";
    if (["Downloading", "Verifying", "Ready", "Error"].includes(updatePhase()) && updatePhase() !== previousPhase) state.updateDismissedVersion = "";
    renderUpdateDialog(); updateVersionLabel();
    if (["Downloading", "Verifying", "Installing"].includes(updatePhase())) startUpdatePolling(); else stopUpdatePolling();
  };
  const beginUpdateCheck = () => { if (state.updateCommandPending !== "" || updateIsBusy()) return; state.updateCommandPending = "check"; send("updates.check", { trigger: "automatic" }); };
  const beginUpdateAction = (action) => {
    if (action === "dismiss") { if (!updateIsBusy()) dismissUpdateDialog(); return; }
    if (state.updateCommandPending !== "" || updateIsBusy()) return;
    if (action === "download") { state.updateDismissedVersion = ""; state.updateCommandPending = "download"; state.updateSnapshot = { ...(state.updateSnapshot ?? {}), phase: "Downloading", canCheck: false, canDownload: false, canInstall: false }; renderUpdateDialog(); updateVersionLabel(); startUpdatePolling(); send("updates.download"); }
    if (action === "install") { state.updateDismissedVersion = ""; state.updateCommandPending = "install"; state.updateSnapshot = { ...(state.updateSnapshot ?? {}), phase: "Installing", canCheck: false, canDownload: false, canInstall: false }; renderUpdateDialog(); updateVersionLabel(); startUpdatePolling(); send("updates.install"); }
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
  const inches = (value) => {
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric.toFixed(2) : "—";
  };
  const panel = (title, body, extra = "") => `<section class="panel ${extra}"><h2 class="panel-title">${title}</h2>${body}</section>`;
  const currentRoute = () => document.querySelector(".nav-item-active")?.dataset.route ?? "books";
  const applicationIsLoading = () => state.applicationLoadState === "loading" || state.applicationLoadState === "refreshing";
  const processIsActive = () => valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false);
  const processMode = (snapshot = window.processSnapshot) => {
    const value = valueFor(snapshot, "mode", null);
    if (value === 2) return "production-interior";
    if (value === 1) return "interior-only";
    if (value === 0) return "full-book";
    const normalized = String(value ?? "").replace(/[_\s]/g, "-").toLowerCase();
    if (normalized === "productioninterior" || normalized === "production-interior") return "production-interior";
    if (normalized === "interioronly" || normalized === "interior-only") return "interior-only";
    if (normalized === "fullbook" || normalized === "full-book") return "full-book";
    return "interior-only";
  };
  const productionInteriorIsRunning = () =>
    (state.processStartPending && state.productionFinalBuildActive)
    || (processIsActive() && processMode() === "production-interior");
  const processStartedAt = (snapshot) => {
    const value = valueFor(snapshot, "startedAt", "");
    const time = value ? new Date(value).getTime() : Number.NaN;
    return Number.isFinite(time) ? time : null;
  };
  const isStaleProcessSnapshot = (snapshot) => {
    const current = processStartedAt(window.processSnapshot);
    const incoming = processStartedAt(snapshot);
    return current !== null && incoming !== null && incoming < current;
  };
  const cacheCleanupBlocked = () => applicationIsLoading() || processIsActive() || state.cacheCleanupActive || Boolean(state.productionActionTaskId);
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
    if (state.productionRefreshAwaitingSnapshot && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
      updateGlobalRefreshControl();
      updateProductionInteractionUi();
    } else {
      render(currentRoute(), false);
    }
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
      if (taskId === state.bookInteriorSaveTaskId) state.bookInteriorSaveAwaitingSnapshot = true;
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
  const pollProductionAction = () => {
    if (state.productionActionTaskId) send("task.get", { taskId: state.productionActionTaskId });
  };
  const productionActionActive = () => Boolean(state.productionActionTaskId);
  const updateProductionInteractionUi = () => {
    const workspace = document.querySelector(".production-workspace");
    if (!workspace) return;
    const taskBusy = productionActionActive();
    const controlsBusy = taskBusy || processIsActive() || state.cacheCleanupActive || applicationIsLoading();
    const finalBusy = productionInteriorIsRunning();
    workspace.setAttribute("aria-busy", String(taskBusy || finalBusy));
    const feedback = workspace.querySelector?.("[data-production-feedback]");
    if (feedback) {
      feedback.hidden = !state.productionFeedback;
      feedback.textContent = state.productionFeedback;
      feedback.classList.toggle("is-error", state.productionFeedbackError);
      feedback.setAttribute("role", state.productionFeedbackError ? "alert" : "status");
    }
    workspace.querySelectorAll?.('[data-action="upload-production-asset"]').forEach((button) => {
      const importing = state.productionImportPending === button.dataset.productionAsset;
      button.disabled = controlsBusy || importing;
      button.textContent = importing ? "Selecting…" : button.dataset.productionIdleLabel;
    });
    workspace.querySelectorAll?.('[data-action="start-production-action"]').forEach((button) => {
      const active = taskBusy && state.productionActionName === button.dataset.productionAction;
      button.disabled = button.dataset.productionSourceExists !== "true" || controlsBusy;
      button.textContent = active ? "Working…" : button.dataset.productionIdleLabel;
      button.setAttribute("aria-busy", String(active));
    });
    const finalButton = workspace.querySelector?.('[data-action="build-final-interior"]');
    if (finalButton) {
      finalButton.disabled = finalButton.dataset.productionReady !== "true" || processIsActive() || state.processStartPending || applicationIsLoading();
      finalButton.textContent = finalBusy ? "Building…" : "Build Final Interior";
      finalButton.setAttribute("aria-busy", String(finalBusy));
    }
  };
  const observeProductionAction = (task) => {
    const taskId = valueFor(task, "taskId", "");
    if (!taskId) return;
    const taskState = valueFor(task, "state", "Queued");
    state.productionActionTaskId = taskId;
    state.productionFeedback = valueFor(task, "step", "Preparing Production asset…") || "Preparing Production asset…";
    state.productionFeedbackError = false;
    if (["Queued", "Running", "Cancelling"].includes(taskState)) {
      if (state.productionActionPollTimer === null) state.productionActionPollTimer = window.setInterval(pollProductionAction, 250);
      if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      return;
    }
    if (state.productionActionPollTimer !== null && window.clearInterval) window.clearInterval(state.productionActionPollTimer);
    state.productionActionPollTimer = null;
    state.productionActionTaskId = "";
    state.productionActionName = "";
    state.productionFeedbackError = taskState === "Failed";
    state.productionFeedback = taskState === "Completed"
      ? "Production action completed. Refreshing status…"
      : taskState === "Cancelled"
        ? "Production action cancelled."
        : valueFor(task, "errorMessage", "Production action failed. The previous asset or PDF was kept.");
    if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
    if (taskState === "Completed") {
      state.productionRefreshAwaitingSnapshot = true;
      beginApplicationRefresh();
    }
  };
  const selectedBook = () => books().find((book) => bookId(book) === state.selectedBookId);
  const assignmentReadiness = (summary) => {
    const assigned = assignedBrandName(summary);
    if (!assigned) return { ready: false, reason: "Assign a Brand to this Book before continuing." };
    if (assignmentStatus(summary) !== "Valid") return { ready: false, reason: valueFor(summary, "assignmentReason", "This Book's Brand assignment is invalid. Reassign or unassign it before continuing.") };
    const brand = assignedBrandFor(summary);
    if (!brand) return { ready: false, reason: `Assigned Brand '${assigned}' is no longer available. Reassign this Book before continuing.` };
    return { ready: true, reason: "", brand };
  };
  const assignedBrandExecutionReadiness = (summary) => {
    const assignment = assignmentReadiness(summary);
    if (!assignment.ready) return assignment;
    if (!isValidatedBrand(assignment.brand)) return { ready: false, reason: `Validate assigned Brand '${assignedBrandName(summary)}' before continuing.`, brand: assignment.brand };
    return assignment;
  };
  const brandTemplateCopyReadiness = (book, summary) => {
    if (!book || !summary || valueFor(summary, "validationStatus", "") !== "Ready") return { ready: false, reason: "Run Interior preflight and resolve Book errors first." };
    const assignment = assignedBrandExecutionReadiness(summary);
    if (!assignment.ready) return assignment;
    const brand = assignment.brand;
    return { ready: true, reason: `Copy cover.psd, app_plus.psd, and book_owner.psd from ${valueFor(brand, "name", "the assigned Brand")}.`, brand };
  };
  const assetsFor = (summary) => valueFor(summary, "assets", []);
  const assetForReference = (summary, sourceReference) => assetsFor(summary).find((asset) => valueFor(asset, "sourceReference", "") === sourceReference);
  const interiorDraftFor = (id, create = false) => {
    let draft = state.bookInteriorDrafts.get(id);
    if (!draft && create) { draft = { assets: new Map() }; state.bookInteriorDrafts.set(id, draft); }
    return draft ?? null;
  };
  const clearInteriorDraft = (id) => state.bookInteriorDrafts.delete(id);
  const clearArtworkBulkSelection = () => {
    state.selectedArtworkReferences.clear();
    state.assetBulkActive = "unchanged";
    state.assetBulkFrameMode = "unchanged";
  };
  const hasInteriorDraft = (id) => {
    const draft = interiorDraftFor(id);
    return Boolean(draft && (draft.hasBackground !== undefined || draft.hasIntro !== undefined || draft.introSourceReferences !== undefined || draft.assets.size));
  };
  const effectiveBackground = (book, summary) => {
    const draft = interiorDraftFor(bookId(book));
    return draft?.hasBackground ?? valueFor(summary, "hasBackground", true);
  };
  const effectiveInteriorAsset = (book, asset) => {
    const change = interiorDraftFor(bookId(book))?.assets.get(valueFor(asset, "sourceReference", ""));
    return {
      isActive: change?.active ?? valueFor(asset, "isActive", true),
      frameMode: change?.frameMode ?? frameModeValue(valueFor(asset, "frameMode", "disabled"))
    };
  };
  const trimEmptyInteriorDraft = (id, draft) => { if (draft.hasBackground === undefined && draft.hasIntro === undefined && draft.introSourceReferences === undefined && draft.assets.size === 0) clearInteriorDraft(id); };
  const stageBackgroundChange = (book, summary, enabled) => {
    const id = bookId(book);
    const draft = interiorDraftFor(id, true);
    if (enabled === valueFor(summary, "hasBackground", true)) delete draft.hasBackground;
    else draft.hasBackground = enabled;
    trimEmptyInteriorDraft(id, draft);
  };
  const stageInteriorAssetChange = (book, asset, field, value) => {
    const id = bookId(book);
    const reference = valueFor(asset, "sourceReference", "");
    const draft = interiorDraftFor(id, true);
    const change = draft.assets.get(reference) ?? {};
    const original = field === "active" ? valueFor(asset, "isActive", true) : frameModeValue(valueFor(asset, "frameMode", "disabled"));
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
  const introTemplateAssetId = (asset, brandName = "") => encodeURIComponent(`${brandName}\u0000${valueFor(asset, "key", "")}`);
  const finalInteriorPageSize = () => {
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    return {
      width: Number(valueFor(settings, "finalPageWidth", 2588)),
      height: Number(valueFor(settings, "finalPageHeight", 2625))
    };
  };
  const introTemplateSizeDescription = () => {
    const finalPage = finalInteriorPageSize();
    return `1024 × 1024, 2048 × 2048, or ${finalPage.width} × ${finalPage.height} pixels`;
  };
  const isSupportedIntroTemplateSize = (width, height) => {
    const finalPage = finalInteriorPageSize();
    return (width === 1024 && height === 1024) ||
      (width === 2048 && height === 2048) ||
      (width === finalPage.width && height === finalPage.height);
  };
  const introReadiness = (book, summary) => {
    const selection = effectiveIntro(book, summary);
    const customCandidates = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    if (selection.hasIntro) {
      if (!selection.sourceReferences.length) return { ready: false, reason: "Custom Intro mode needs at least one selected Book interior page." };
      const sources = new Set(customCandidates.map((asset) => String(valueFor(asset, "sourceReference", "")).toLowerCase()));
      if (selection.sourceReferences.some((reference) => !sources.has(String(reference).toLowerCase()))) return { ready: false, reason: "A selected custom Intro page is missing from Book interior." };
      return { ready: true, reason: "" };
    }
    const assignment = assignmentReadiness(summary);
    if (!assignment.ready) return assignment;
    const brand = assignment.brand;
    const templates = (valueFor(brand, "introTemplateAssets", []) ?? []).filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", "")));
    if (!templates.length) return { ready: false, reason: `Assigned Brand '${assignedBrandName(summary)}' has no eligible Intro templates.` };
    const effectiveTemplates = templates;
    if (effectiveTemplates.some((asset) => state.introTemplateDimensions.get(introTemplateAssetId(asset, valueFor(brand, "name", "")))?.valid === false)) return { ready: false, reason: `An effective Intro template is unreadable or must be ${introTemplateSizeDescription()}.` };
    return { ready: true, reason: "" };
  };
  const processingReadiness = (book, summary) => {
    if (!workspaceStateAvailable(summary)) return { ready: false, reason: valueFor(summary, "workspaceStateError", "Workspace state is unavailable. Restore or repair the state file, then refresh.") };
    if (hasInteriorDraft(bookId(book))) return { ready: false, reason: "Save Interior changes before processing." };
    if (valueFor(summary, "validationStatus", "Needs review") !== "Ready") return { ready: false, reason: "Run Interior preflight until this Book is ready." };
    const assignment = assignedBrandExecutionReadiness(summary);
    if (!assignment.ready) return assignment;
    return introReadiness(book, summary);
  };
  const productionSummaryFor = (summary) => valueFor(summary, "production", { assets: [] });
  const productionAssetFor = (summary, kind) => valueFor(productionSummaryFor(summary), "assets", [])
    .find((asset) => valueFor(asset, "assetKind", "") === kind) ?? null;
  const productionFinalReadiness = (book, summary) => {
    if (hasInteriorDraft(bookId(book))) return { ready: false, reason: "Save changes before building Final Interior." };
    const standard = processingReadiness(book, summary);
    if (!standard.ready) return standard;
    const brand = standard.brand ?? assignedBrandFor(summary);
    const missing = [["interior-cover", "Interior Cover"], ["book-owner", "Book Owner"]]
      .filter(([kind]) => valueFor(productionAssetFor(summary, kind), "sourceStatus", "Missing") === "Missing")
      .map(([, label]) => label);
    if (missing.length) return { ready: false, reason: `Upload ${missing.join(" and ")} before building Final Interior.` };
    if (processIsActive() || state.processStartPending) return { ready: false, reason: productionInteriorIsRunning() ? "Build Final Interior is already running." : "Process Interior is running." };
    if (productionActionActive()) return { ready: false, reason: "Wait for the active Production action to finish." };
    if (state.cacheCleanupActive) return { ready: false, reason: "Wait for Clear Cache to finish." };
    return { ready: true, reason: "Build a fresh Production Interior from current sources and saved settings.", brand };
  };

  const selectedProcessingReadiness = () => {
    const selected = [...state.selectedBookIds]
      .map((id) => books().find((book) => bookId(book) === id))
      .filter(Boolean);
    for (const book of selected) {
      const readiness = processingReadiness(book, summaryFor(book));
      if (!readiness.ready) return { ready: false, reason: `${valueFor(book, "name", "Book")}: ${readiness.reason}`, book };
    }
    const groups = new Map();
    selected.forEach((book) => {
      const brandName = assignedBrandName(summaryFor(book));
      groups.set(brandName, (groups.get(brandName) ?? 0) + 1);
    });
    if (groups.size > 1) {
      const detail = [...groups.entries()].map(([brand, count]) => `${brand}: ${count}`).join("; ");
      return { ready: false, reason: `Selected Books belong to different Brands (${detail}). Filter and process one Brand at a time.`, mixed: true };
    }
    return { ready: selected.length > 0, reason: selected.length ? "" : "Select at least one Book.", brandName: groups.keys().next().value ?? "" };
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
    document.querySelectorAll('[data-action="set-book-background"], [data-action="set-intro-mode"], [data-action="intro-add-template"], [data-action="intro-remove-template"], [data-action="intro-move-template"], [data-action="toggle-artwork-selection"], [data-action="toggle-all-artwork"], [data-action="set-artwork-bulk-active"], [data-action="set-artwork-bulk-frame-mode"], [data-action="apply-artwork-bulk"]').forEach((control) => { control.disabled = controlsDisabled || control.dataset.customIntro === "true"; });
    const indicator = document.querySelector("[data-book-interior-unsaved]");
    if (indicator) indicator.hidden = !dirty;
  };
  const localImageMarkup = (asset, alt, fallback = "Preview unavailable") => {
    const url = valueFor(asset, "localImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" width="256" height="256" loading="lazy" decoding="async" data-local-image data-image-fallback="${escapeHtml(fallback)}">`
      : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;
  };
  const introTemplateImageMarkup = (asset, alt, brandName, fallback = "Image unavailable") => {
    const url = valueFor(asset, "localImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" width="256" height="256" loading="lazy" decoding="async" data-local-image data-intro-template-id="${escapeHtml(introTemplateAssetId(asset, brandName))}" data-image-fallback="${escapeHtml(fallback)}">`
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
  const processStepKey = (step) => String(step ?? "").trim().toLowerCase();
  const processStepLabel = (mode, step) => {
    const key = processStepKey(step);
    const shared = {
      queued: "Preparing",
      preparing: "Preparing",
      "intro-pages": "Intro pages",
      "interior-pages": "Interior pages",
      assembly: "Validating pages",
      completed: "Completed",
      cancelled: "Cancelled",
      failed: "Failed",
      cancelling: "Cancelling"
    };
    if (mode === "production-interior" || mode === "full-book") {
      return {
        ...shared,
        "production-prefix-pages": "Production pages",
        "interior-pdf-export": "PDF export",
        "pdf-export": "PDF export",
        "interior-publish": "Publishing",
        publish: "Publishing"
      }[key] ?? step ?? "Preparing";
    }
    return { ...shared, "book.completed": "Saving previews" }[key] ?? step ?? "Preparing";
  };
  const processStageModel = (mode, step, terminalStep = "") => {
    const production = mode === "production-interior" || mode === "full-book";
    const stages = production
      ? ["Preparing", "Production pages", "Intro pages", "Interior pages", "Validating pages", "PDF export", "Publishing"]
      : ["Preparing", "Intro pages", "Interior pages", "Validating pages", "Saving previews"];
    if (terminalStep === "Completed") return { stages, index: stages.length };
    const label = processStepLabel(mode, step);
    const index = stages.indexOf(label);
    return { stages, index: index >= 0 ? index : 0 };
  };
  const updateGlobalProcessStatus = () => {
    const control = document.getElementById("global-process-status");
    if (!control) return;
    const snapshot = window.processSnapshot;
    const active = valueFor(snapshot, "isActive", false);
    const cancelling = valueFor(snapshot, "isCancelling", false);
    const step = processStepLabel(processMode(snapshot), valueFor(snapshot, "currentStep", "Preparing"));
    const activeLabel = processMode(snapshot) === "production-interior" ? `Building Final Interior · ${step}` : `Process Interior · ${step}`;
    control.classList?.toggle("is-active", active && !cancelling);
    control.classList?.toggle("is-cancelling", cancelling);
    control.innerHTML = `<span class="status-dot"></span><span>${escapeHtml(cancelling ? "Stopping processing" : active ? activeLabel : "Nothing processing")}</span>`;
  };

  const renderConfiguration = () => {
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    const setting = (name, fallback) => valueFor(settings, name, fallback);
    const grouped = (group, name, fallback) => valueFor(valueFor(settings, group, {}), name, fallback);
    const detectionInput = (label, name, fallback, extra = "") => `<label class="field"><span>${label}</span><input class="control" data-setting-group="borderLineDetection" data-setting="${name}" type="number" ${extra} value="${grouped("borderLineDetection", name, fallback)}"></label>`;
    content.innerHTML = `<div class="page-header"><div><h1>Configuration</h1><p>Manage global application settings.</p></div><div class="page-actions">${refreshAction("Load")}<button class="button-primary" data-action="save-settings">Save</button></div></div><div class="detail-stack">${panel("Application", `<div class="form-grid two"><label class="field"><span>Maximum concurrency</span><input class="control" data-setting="maximumPageConcurrency" type="number" min="1" max="12" value="${setting("maximumPageConcurrency", 4)}"></label><label class="field"><span>Artwork dark threshold</span><input class="control" data-setting="artworkDetectionThreshold" type="number" min="0" max="255" value="${setting("artworkDetectionThreshold", 20)}"></label></div>`)}${panel("Interior processing", `<p class="hint">Working Area is the square processing canvas. Final Interior Page is the exported raster and determines the Interior PDF size at the configured DPI.</p><div class="form-grid three"><label class="field"><span>Max artwork side (px)</span><input class="control" data-setting="artworkMaximumSide" type="number" min="1" value="${setting("artworkMaximumSide", 2270)}"></label><label class="field"><span>Working Area width (px)</span><input class="control" data-setting="workingPageWidth" type="number" min="1" value="${setting("workingPageWidth", 2550)}"></label><label class="field"><span>Working Area height (px)</span><input class="control" data-setting="workingPageHeight" type="number" min="1" value="${setting("workingPageHeight", 2550)}"></label><label class="field"><span>Final Interior Page width (px)</span><input class="control" data-setting="finalPageWidth" type="number" min="1" value="${setting("finalPageWidth", 2588)}"></label><label class="field"><span>Final Interior Page height (px)</span><input class="control" data-setting="finalPageHeight" type="number" min="1" value="${setting("finalPageHeight", 2625)}"></label><label class="field"><span>Final Interior Page DPI</span><input class="control" data-setting="dpi" type="number" min="1" value="${setting("dpi", 300)}"></label></div>`)}${panel("Advanced artwork detection", `<div class="form-grid three"><label class="field"><span>Normalized source size</span><input class="control" data-setting-group="artworkSourceNormalization" data-setting="normalizedSourceSize" type="number" min="1" value="${grouped("artworkSourceNormalization", "normalizedSourceSize", 2048)}"></label>${detectionInput("Pass 1 depth", "pass1SearchDepth", 200, "min=1")}${detectionInput("Pass 2 depth", "pass2SearchDepth", 320, "min=1")}${detectionInput("Corner padding", "cornerSearchPadding", 40, "min=0")}${detectionInput("Track tolerance", "trackDepthTolerance", 6, "min=0")}${detectionInput("Corner-line tolerance", "cornerLineTolerance", 16, "min=0")}${detectionInput("Max depth spread", "maximumDepthSpread", 24, "min=0")}${detectionInput("Segments", "segmentCount", 8, "min=1")}${detectionInput("Corner exclusion ratio", "cornerExclusionRatio", .10, "min=0 max=1 step=0.01")}${detectionInput("Compatible corners", "minimumCompatibleCorners", 3, "min=1 max=4")}${detectionInput("Min segment support", "minimumSegmentSupportRatio", .35, "min=0 max=1 step=0.01")}${detectionInput("Min side support", "minimumSideSupportRatio", .55, "min=0 max=1 step=0.01")}${detectionInput("Min span", "minimumSpanRatio", .70, "min=0 max=1 step=0.01")}${detectionInput("Supported segments", "minimumSupportedSegments", 6, "min=1")}${detectionInput("Missing segment run", "maximumMissingSegmentRun", 2, "min=0")}</div>`)}</div>`;
  };

  const brandListMarkup = (brands) => brands.length
    ? brands.map((brand) => `<li class="${valueFor(brand, "name", "") === state.inspectedBrand ? "selected" : ""}" data-action="select-brand" data-brand-name="${escapeHtml(valueFor(brand, "name", ""))}"><span>${escapeHtml(valueFor(brand, "name", ""))}</span>${badge(brandValidationStatus(valueFor(brandSummaryFor(brand), "validationStatus", "NotValidated")))}</li>`).join("")
    : "<li class=\"empty-row\">No matching Brands found.</li>";
  const refreshBrandList = () => {
    const search = state.brandFilter.trim().toLocaleLowerCase();
    const brands = valueFor(discovery(), "brands", []).filter((brand) => !search || valueFor(brand, "name", "").toLocaleLowerCase().includes(search));
    const list = content.querySelector("[data-brand-list]");
    if (list) list.innerHTML = brandListMarkup(brands);
  };

  const renderBrands = () => {
    const availableBrands = valueFor(discovery(), "brands", []);
    if (!state.inspectedBrand && availableBrands.length) state.inspectedBrand = valueFor(availableBrands[0], "name", "");
    const selected = availableBrands.find((brand) => valueFor(brand, "name", "") === state.inspectedBrand);
    const search = state.brandFilter.trim().toLocaleLowerCase();
    const allBrands = availableBrands.filter((brand) => !search || valueFor(brand, "name", "").toLocaleLowerCase().includes(search));
    const assets = valueFor(selected, "assets", []);
    const selectedValidation = brandSummaryFor(selected);
    const validationStatus = brandValidationStatus(valueFor(selectedValidation, "validationStatus", "NotValidated"));
    const validationResult = valueFor(state.brandValidationResult, "brandName", "") === state.inspectedBrand ? state.brandValidationResult : null;
    const validationFailures = valueFor(validationResult, "failures", []);
    const dimensions = (value) => {
      const width = valueFor(value, "width", 0);
      const height = valueFor(value, "height", 0);
      return width && height ? `${width} × ${height} px` : "—";
    };
    const size = (asset) => dimensions(valueFor(asset, "size", null));
    const targetFor = (asset, entry = null) => entry ? `${valueFor(asset, "name", "")}/${valueFor(entry, "name", "")}` : valueFor(asset, "name", "");
    const failureFor = (asset, entry = null) => validationFailures.find((failure) => String(valueFor(failure, "target", "")).replaceAll("\\", "/").toLocaleLowerCase() === targetFor(asset, entry).toLocaleLowerCase());
    const statusFor = (asset, entry = null) => {
      if (failureFor(asset, entry)) return "Needs attention";
      if (!entry && validationFailures.some((failure) => String(valueFor(failure, "target", "")).replaceAll("\\", "/").toLocaleLowerCase().startsWith(`${targetFor(asset).toLocaleLowerCase()}/`))) return "Needs attention";
      return valueFor(entry ?? asset, "status", "Missing");
    };
    const expectedSize = (asset) => {
      const name = valueFor(asset, "name", "");
      const requirement = valueFor(window.appSnapshot, "brandImageSizeRequirements", [])
        .find((item) => valueFor(item, "target", "") === name);
      const allowedSizes = valueFor(requirement, "allowedSizes", []);
      if (allowedSizes.length) return allowedSizes.map(dimensions).join(", ");

      // The desktop may show a cached snapshot while its host finishes loading.
      // These two file contracts are direct projections of global settings.
      const settings = valueFor(window.appSnapshot, "globalSettings", {});
      if (name === "frame.png") {
        const maximumSide = valueFor(settings, "artworkMaximumSide", null);
        return maximumSide ? `${maximumSide} × ${maximumSide} px` : "—";
      }
      if (name === "background.png") {
        const width = valueFor(settings, "finalPageWidth", null);
        const height = valueFor(settings, "finalPageHeight", null);
        return width && height ? `${width} × ${height} px` : "—";
      }
      return "Validate Brand to confirm the required size";
    };
    const validationMessage = validationResult && !valueFor(validationResult, "isSuccess", false)
      ? `<section class="brand-validation-summary" role="alert" tabindex="-1" data-brand-validation-summary aria-labelledby="brand-validation-title"><h3 id="brand-validation-title">Fix these Brand assets</h3><p>Each item names the file, its current size, and the size required before processing.</p><ul>${validationFailures.map((failure) => `<li><strong>${escapeHtml(valueFor(failure, "target", "Brand asset"))}</strong><span>${escapeHtml(valueFor(failure, "message", "Validation failed."))}</span></li>`).join("")}</ul></section>`
      : "";
    const renderFolder = (asset) => {
      const entries = valueFor(asset, "entries", []);
      return `<section class="brand-folder"><div class="brand-asset-heading"><div><h3>${escapeHtml(valueFor(asset, "name", ""))}</h3><p>${escapeHtml(valueFor(asset, "type", "Folder"))} · ${entries.length} file${entries.length === 1 ? "" : "s"}</p></div>${badge(statusFor(asset))}</div><table class="data-table brand-folder-table"><thead><tr><th>Name</th><th>Extension</th><th>Size</th><th>Status</th></tr></thead><tbody>${entries.length ? entries.map((entry) => { const failure = failureFor(asset, entry); return `<tr><td>${escapeHtml(valueFor(entry, "name", ""))}${failure ? `<small class="brand-asset-error">${escapeHtml(valueFor(failure, "message", "Validation failed."))}</small>` : ""}</td><td>${escapeHtml(valueFor(entry, "extension", "—") || "—")}</td><td>${escapeHtml(size(entry))}</td><td>${badge(statusFor(asset, entry))}</td></tr>`; }).join("") : "<tr><td colspan=\"4\" class=\"empty-copy\">No files found.</td></tr>"}</tbody></table>${valueFor(asset, "name", "") === "IntroTemplate" ? `<p class="brand-size-note">Required image size: ${expectedSize(asset)}.</p>` : ""}</section>`;
    };
    const renderFile = (asset) => {
      const failure = failureFor(asset);
      const isImage = valueFor(asset, "type", "") === "Image";
      const details = isImage ? `<dl><div><dt>Current size</dt><dd>${escapeHtml(size(asset))}</dd></div><div><dt>Required size</dt><dd>${escapeHtml(expectedSize(asset))}</dd></div></dl>` : "";
      return `<article class="brand-file-card"><div class="brand-asset-heading"><div><h3>${escapeHtml(valueFor(asset, "name", ""))}</h3><p>${isImage ? escapeHtml(valueFor(asset, "extension", "") || "Image") : "Required PSD template"}</p></div>${badge(statusFor(asset))}</div>${details}${failure ? `<p class="brand-asset-error">${escapeHtml(valueFor(failure, "message", "Validation failed."))}</p>` : ""}</article>`;
    };
    const folders = assets.filter((asset) => valueFor(asset, "type", "") === "Folder");
    const files = assets.filter((asset) => valueFor(asset, "type", "") !== "Folder");
    const assetInventory = assets.length ? `<div class="brand-asset-inventory"><div class="brand-folder-list">${folders.map(renderFolder).join("")}</div><div class="brand-file-grid">${files.map(renderFile).join("")}</div></div>` : "<p class=\"empty-copy\">No brand assets found.</p>";
    const authorDraft = selected ? brandAuthorDraftFor(selected) : "";
    const authorDirty = selected ? brandAuthorIsDirty(selected, authorDraft) : false;
    const impactedBooks = selected && authorDirty ? books().filter((book) => {
      const summary = summaryFor(book);
      return assignedBrandName(summary) === valueFor(selected, "name", "") && !authorMatches(valueFor(metadataFor(summary), "author", ""), authorDraft);
    }).length : 0;
    const brandInfo = selected ? `<section class="catalog-card"><div class="catalog-card-heading"><div><h3>Brand Information</h3><p>MVP contract: one Brand has one Primary Author.</p></div>${badge(valueFor(selectedValidation, "metadataStatus", "Missing"))}</div><label class="field"><span>Author</span><input class="control" data-action="brand-author-input" data-brand-name="${escapeHtml(valueFor(selected, "name", ""))}" value="${escapeHtml(authorDraft)}" placeholder="Unknown" autocomplete="off"></label>${impactedBooks ? `<p class="catalog-warning" role="alert">Saving this Author will make ${impactedBooks} assigned Book${impactedBooks === 1 ? "" : "s"} invalid. Their assignments will be kept for review.</p>` : ""}<div class="catalog-actions"><p class="catalog-feedback ${state.catalogFeedbackError ? "is-error" : ""}" role="${state.catalogFeedbackError ? "alert" : "status"}">${state.catalogMutationTarget === valueFor(selected, "name", "") ? escapeHtml(state.catalogFeedback) : ""}</p><button class="button-primary" data-action="save-brand-author" data-brand-name="${escapeHtml(valueFor(selected, "name", ""))}" ${!authorDirty || state.catalogMutationPending || processIsActive() ? "disabled" : ""}>${state.catalogMutationPending && state.catalogMutationCommand === "brand.author.save" ? "Saving…" : "Save Author"}</button></div></section>` : "";
    content.innerHTML = `<div class="page-header"><div><h1>Brands & templates</h1><p>Inspect reusable Brand assets and resolve exact file requirements before processing.</p></div></div><div class="master-detail"><section class="panel list-panel"><label class="brand-search"><span>Search Brands</span><input class="control" data-action="filter-brands" value="${escapeHtml(state.brandFilter)}" placeholder="Search by name" /></label><div class="list-title">Brands</div><ul class="item-list" data-brand-list>${brandListMarkup(allBrands)}</ul></section><section class="detail-pane">${selected ? panel(escapeHtml(valueFor(selected, "name", "")), `${brandInfo}<div class="page-actions brand-validation-actions"><div>${badge(validationStatus)}<p class="panel-note">Validate IntroTemplate, frame.png, background.png, cover.psd, app_plus.psd, and book_owner.psd before processing.</p></div><button class="button-primary" data-action="validate-brand" ${processIsActive() ? "disabled" : ""}>Validate Brand</button></div>${validationMessage}${assetInventory}`) : panel("Brand detail", "<p class=\"empty-copy\">Select a Brand to inspect its assets.</p>")}</section></div>`;
  };

  const renderProcessedInteriorPages = (summary) => {
    const pages = [...valueFor(summary, "interiorPages", [])]
      .filter((page) => displayStatus(valueFor(page, "status", "")) === "Completed")
      .sort((left, right) => String(valueFor(left, "pageId", "")).localeCompare(String(valueFor(right, "pageId", "")), undefined, { numeric: true, sensitivity: "base" }));
    if (!pages.length) return `<section class="processed-interior-pages"><div class="processed-interior-pages-empty"><h3>No processed pages</h3><p>Process Interior to create preview pages for this Book.</p></div></section>`;
    const tile = (page, index) => {
      const pageId = valueFor(page, "pageId", `page-${index + 1}`);
      return `<figure class="processed-interior-page"><div class="processed-interior-page-preview">${localImageMarkup(page, `Processed Interior page ${index + 1}`, "Preview unavailable")}</div><figcaption><strong>Page ${index + 1}</strong><span title="${escapeHtml(pageId)}">${escapeHtml(pageId)}</span></figcaption></figure>`;
    };
    return `<section class="processed-interior-pages"><header class="processed-interior-pages-heading"><div><h3>Interior pages</h3><p>Read-only previews created by the most recent Interior Processing run.</p></div><span class="interior-artwork-count" role="status"><strong>${pages.length}</strong> processed</span></header><div class="processed-interior-pages-scroll"><div class="processed-interior-pages-grid">${pages.map(tile).join("")}</div></div></section>`;
  };

  const renderProductionImage = (url, alt, fallback, className = "") => url
    ? `<img class="${className}" src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" loading="lazy" decoding="async" data-local-image data-image-fallback="${escapeHtml(fallback)}">`
    : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;

  const renderProductionWorkspace = (book, summary) => {
    const production = productionSummaryFor(summary);
    const assets = valueFor(production, "assets", []);
    const taskBusy = productionActionActive();
    const controlsBusy = taskBusy || processIsActive() || state.cacheCleanupActive || applicationIsLoading();
    const actionFor = (kind) => kind === "final-cover" ? "build-cover-pdf" : kind === "interior-cover" ? "process-interior-cover" : "process-book-owner";
    const actionLabel = (kind) => kind === "final-cover" ? "Build Cover PDF" : kind === "interior-cover" ? "Process Interior Cover" : "Process Book Owner";
    const card = (asset) => {
      const kind = valueFor(asset, "assetKind", "");
      const label = valueFor(asset, "displayName", "Production asset");
      const sourceStatus = valueFor(asset, "sourceStatus", "Missing");
      const processedStatus = valueFor(asset, "processedStatus", "Not applicable");
      const exists = sourceStatus !== "Missing";
      const importing = state.productionImportPending === kind;
      const activeAction = taskBusy && state.productionActionName === actionFor(kind);
      const sourcePreview = renderProductionImage(valueFor(asset, "sourceLocalImageUrl", ""), `${label} source`, `Upload ${label}`, "production-source-image");
      const processedPreview = kind === "final-cover" ? "" : `<div class="production-processed-preview"><span>Processed</span>${renderProductionImage(valueFor(asset, "processedLocalImageUrl", ""), `Processed ${label}`, "Not processed", "production-processed-image")}</div>`;
      const actionStatus = kind === "final-cover" ? valueFor(production, "coverOutputStatus", "Missing") : processedStatus;
      const actionDisabled = !exists || controlsBusy || importing;
      const reason = !exists ? `Upload ${label} first.` : controlsBusy ? "Another processing action is active." : `${actionLabel(kind)} from the current canonical PNG.`;
      return `<article class="production-asset-card production-asset-${kind}"><header><div><h3>${escapeHtml(label)}</h3><p>${escapeHtml(valueFor(asset, "fileName", ""))}</p></div>${badge(sourceStatus)}</header><div class="production-preview ${kind === "final-cover" ? "production-preview-cover" : ""}">${sourcePreview}</div>${processedPreview}<dl><div><dt>Derived output</dt><dd>${badge(actionStatus)}</dd></div><div><dt>Last processed</dt><dd>${dateTime(kind === "final-cover" ? valueFor(production, "coverBuiltAtUtc", null) : valueFor(asset, "processedAtUtc", null))}</dd></div></dl><div class="production-card-actions"><button class="button-secondary" data-action="upload-production-asset" data-production-asset="${kind}" data-production-idle-label="${exists ? "Replace" : "Upload"}" data-book-id="${escapeHtml(bookId(book))}" ${controlsBusy || importing ? "disabled" : ""}>${importing ? "Selecting…" : exists ? "Replace" : "Upload"}</button><button class="button-primary" data-action="start-production-action" data-production-action="${actionFor(kind)}" data-production-source-exists="${exists}" data-production-idle-label="${actionLabel(kind)}" data-book-id="${escapeHtml(bookId(book))}" aria-describedby="production-help-${kind}" aria-busy="${activeAction}" ${actionDisabled ? "disabled" : ""}>${activeAction ? "Working…" : actionLabel(kind)}</button></div><p class="production-action-help" id="production-help-${kind}">${escapeHtml(reason)}${kind === "final-cover" ? " Replaces the current Cover PDF only after a successful build." : " Final Interior remains unchanged."}</p></article>`;
    };
    const readiness = productionFinalReadiness(book, summary);
    const feedback = `<p class="production-feedback ${state.productionFeedbackError ? "is-error" : ""}" data-production-feedback role="${state.productionFeedbackError ? "alert" : "status"}" ${state.productionFeedback ? "" : "hidden"}>${escapeHtml(state.productionFeedback)}</p>`;
    const background = effectiveBackground(book, summary) ? "Enabled" : "Disabled";
    const finalBusy = productionInteriorIsRunning();
    const finalBlocked = processIsActive() || state.processStartPending || applicationIsLoading();
    return `<section class="production-workspace" aria-busy="${taskBusy || finalBusy}"><header class="production-summary"><div><h3>Production Assets</h3><p>Assigned Brand: <strong>${escapeHtml(assignedBrandName(summary) || "Unassigned")}</strong> · Background: <strong>${background}</strong></p></div><div>${badge(valueFor(production, "interiorOutputKind", "Legacy"))} ${badge(valueFor(production, "interiorOutputStatus", "Missing"))}</div></header>${feedback}<div class="production-asset-grid">${assets.length ? assets.map(card).join("") : "<p class=\"empty-copy\">Production workspace status is unavailable. Refresh the library.</p>"}</div><section class="production-final-action"><div><h3>Final Interior</h3><p>Order: Interior Cover → Book Owner → Intro → randomized Interior. Background pages follow the saved HasBackground setting.</p><p id="production-final-help">${escapeHtml(readiness.reason)} Only Build Final Interior replaces the current Interior PDF. Process Interior refreshes processed-page previews only.</p><small>Current output: ${escapeHtml(valueFor(production, "interiorOutputKind", "Legacy"))} · ${dateTime(valueFor(production, "interiorBuiltAtUtc", null))}</small></div><button class="button-primary" data-action="build-final-interior" data-production-ready="${readiness.ready}" data-book-id="${escapeHtml(bookId(book))}" aria-describedby="production-final-help" aria-busy="${finalBusy}" ${readiness.ready && !finalBlocked ? "" : "disabled"}>${finalBusy ? "Building…" : "Build Final Interior"}</button></section></section>`;
  };

  const renderBookInformation = (book, summary) => {
    const draft = metadataDraftFor(book, summary);
    const dirty = hasMetadataDraft(book, summary);
    const error = subcoverError(draft.subcover);
    const showError = Boolean(error) && state.subcoverTouchedBooks.has(bookId(book));
    const assignmentWarning = metadataAssignmentWarning(draft, summary);
    const disabled = catalogMutationBusy() || processIsActive();
    const field = (label, name, placeholder = "Unknown") => `<label class="field"><span>${label}</span><input class="control" data-action="book-metadata-input" data-metadata-field="${name}" data-book-id="${escapeHtml(bookId(book))}" value="${escapeHtml(draft[name])}" placeholder="${placeholder}" autocomplete="off"></label>`;
    return `<section class="catalog-card" data-book-information-card><div class="catalog-card-heading"><div><h3>Book Information</h3><p>Paste production metadata from your ChatGPT Web analysis. Blank fields remain Unknown.</p></div>${dirty ? '<span class="status-badge status-warn">Unsaved</span>' : ""}</div><div class="catalog-form-grid">${field("Title", "title")}${field("Subtitle", "subtitle")}<label class="field"><span>Subcover</span><input class="control ${showError ? "control-invalid" : ""}" data-action="book-metadata-input" data-metadata-field="subcover" data-book-id="${escapeHtml(bookId(book))}" value="${escapeHtml(draft.subcover)}" placeholder="Unknown" maxlength="99" aria-describedby="book-subcover-error" aria-invalid="${showError}" autocomplete="off"><small id="book-subcover-error" class="field-error" ${showError ? "" : "hidden"}>${escapeHtml(error)}</small></label>${field("Author", "author", "Primary Author or Unknown")}<label class="field catalog-description-field"><span>Description</span><textarea class="control" rows="4" data-action="book-metadata-input" data-metadata-field="description" data-book-id="${escapeHtml(bookId(book))}" placeholder="Unknown">${escapeHtml(draft.description)}</textarea></label></div><p id="book-author-assignment-warning" class="catalog-warning" role="alert" ${assignmentWarning ? "" : "hidden"}>${escapeHtml(assignmentWarning)}</p><div class="catalog-actions"><p class="catalog-feedback ${state.catalogFeedbackError ? "is-error" : ""}" data-catalog-feedback="metadata" role="${state.catalogFeedbackError ? "alert" : "status"}">${state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.metadata.save" ? escapeHtml(state.catalogFeedback) : ""}</p><button class="button-primary" data-action="save-book-metadata" data-book-id="${escapeHtml(bookId(book))}" ${!dirty || error || disabled ? "disabled" : ""}>${state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save" ? "Saving…" : "Save Book Information"}</button></div></section>`;
  };

  const renderBookBrandAssignment = (book, summary) => {
    const assigned = assignedBrandName(summary);
    const currentStatus = assignmentStatus(summary);
    const candidates = matchingBrandsFor(summary);
    const valid = currentStatus === "Valid";
    const unavailableCurrent = assigned && !candidates.some((brand) => valueFor(brand, "name", "") === assigned);
    const options = [`<option value="">Select a matching Brand…</option>`, ...(unavailableCurrent ? [`<option value="${escapeHtml(assigned)}" selected disabled>${escapeHtml(assigned)} — ${escapeHtml(assignmentLabel(summary))}</option>`] : []), ...candidates.map((brand) => `<option value="${escapeHtml(valueFor(brand, "name", ""))}" ${valid && valueFor(brand, "name", "") === assigned ? "selected" : ""}>${escapeHtml(valueFor(brand, "name", ""))}</option>`)].join("");
    const author = valueFor(metadataFor(summary), "author", "");
    const reason = assigned && !valid ? valueFor(summary, "assignmentReason", "Choose another Brand or unassign this Book.") : "";
    const disabled = catalogMutationBusy() || processIsActive();
    return `<section class="catalog-card" data-book-assignment-card><div class="catalog-card-heading"><div><h3>Brand Assignment</h3><p>Only Brands whose Author matches this Book's saved Author are available.</p></div>${badge(assigned ? assignmentLabel(summary) : "Unassigned")}</div><dl class="catalog-assignment-summary"><div><dt>Book Author</dt><dd>${escapeHtml(author || "Unknown")}</dd></div><div><dt>Assigned Brand</dt><dd>${escapeHtml(assigned || "Unassigned")}</dd></div></dl>${reason ? `<p class="catalog-warning" role="alert">${escapeHtml(reason)} The existing assignment is preserved until you choose what to do.</p>` : ""}<label class="field"><span>Select Brand</span><select class="control" data-action="book-brand-select" data-book-id="${escapeHtml(bookId(book))}" ${!author || disabled ? "disabled" : ""}>${options}</select><small>${author ? candidates.length ? `${candidates.length} matching Brand${candidates.length === 1 ? "" : "s"}.` : "No Brand Author matches this Book Author." : "Save a Book Author before assigning a Brand."}</small></label><div class="catalog-actions"><p class="catalog-feedback ${state.catalogFeedbackError ? "is-error" : ""}" data-catalog-feedback="assignment" role="${state.catalogFeedbackError ? "alert" : "status"}">${state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand !== "book.metadata.save" ? escapeHtml(state.catalogFeedback) : ""}</p><div><button class="button-secondary" data-action="unassign-book-brand" data-book-id="${escapeHtml(bookId(book))}" ${!assigned || disabled ? "disabled" : ""}>Unassign</button><button class="button-primary" data-action="assign-book-brand" data-book-id="${escapeHtml(bookId(book))}" disabled>${state.catalogMutationPending && state.catalogMutationCommand === "book.brand.assign" ? "Assigning…" : "Assign Brand"}</button></div></div></section>`;
  };

  const renderBrandTemplateCopyCard = (book, summary) => {
    const readiness = brandTemplateCopyReadiness(book, summary);
    const assigned = assignedBrandName(summary) || "Unassigned";
    return `<section class="asset-background-setting" data-brand-template-copy-card><div><h3>Brand PSD templates</h3><p>Assigned Brand: <strong>${escapeHtml(assigned)}</strong>. Copy cover.psd, app_plus.psd, and book_owner.psd into this Book workspace.</p><small>${escapeHtml(readiness.reason)}</small></div><button class="button-secondary" data-action="copy-brand-templates" data-book-id="${escapeHtml(bookId(book))}" ${readiness.ready && !state.brandTemplateCopyPending ? "" : "disabled"} title="${escapeHtml(readiness.reason)}">${state.brandTemplateCopyPending ? "Copying…" : "Copy Brand Templates"}</button></section>`;
  };

  const refreshProductionWorkspace = (focusSelector = "") => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const workspace = document.querySelector(".production-workspace");
    if (!book || !summary || !workspace) return;
    const drawerBody = document.querySelector(".book-drawer-body");
    if (drawerBody && Number.isFinite(drawerBody.scrollTop)) state.bookDrawerScrollTop = drawerBody.scrollTop;
    workspace.outerHTML = renderProductionWorkspace(book, summary);
    const refreshedDrawerBody = document.querySelector(".book-drawer-body");
    if (refreshedDrawerBody && Number.isFinite(state.bookDrawerScrollTop)) refreshedDrawerBody.scrollTop = state.bookDrawerScrollTop;
    if (focusSelector) document.querySelector(focusSelector)?.focus();
  };

  const renderBookTabs = (book, summary) => {
    if (!workspaceStateAvailable(summary)) {
      return `<div class="book-heading"><div><h2>${escapeHtml(bookDisplayTitle(book, summary))}</h2><p>Folder: ${escapeHtml(valueFor(book, "name", bookId(book)))}</p></div></div><section class="process-failure" role="alert"><strong>Workspace state unavailable</strong><p>${escapeHtml(valueFor(summary, "workspaceStateError", "Restore or repair the workspace state file, then refresh the library."))}</p><p>No Book settings or processing actions are available, and the original state file has not been changed.</p></section>`;
    }
    const tabButton = (id, label) => `<button type="button" id="book-tab-${id}" class="detail-tab ${state.selectedBookTab === id ? "active" : ""}" data-action="book-tab" data-book-tab="${id}" role="tab" aria-selected="${state.selectedBookTab === id}" aria-controls="book-panel-${id}" tabindex="${state.selectedBookTab === id ? "0" : "-1"}">${label}</button>`;
    const readiness = processingReadiness(book, summary);
    const migratedCount = Number(valueFor(summary, "legacyFrameModePageCount", 0)) || 0;
    const migrationWarning = migratedCount > 0
      ? `<section class="catalog-warning" role="status"><strong>Frame mode updated</strong><p>${migratedCount} Interior page${migratedCount === 1 ? "" : "s"} previously using Auto now use No Frame. Review Interior artwork before reprocessing.</p><button type="button" class="button-secondary" data-action="book-tab" data-book-tab="artwork">Review Interior artwork</button></section>`
      : "";
    const body = state.selectedBookTab === "production"
      ? renderProductionWorkspace(book, summary)
      : state.selectedBookTab === "settings"
      ? `<section class="interior-settings"><section class="asset-background-setting"><div><h3>Brand background</h3><p>Insert the assigned Brand background after every active Interior page.</p></div><label class="asset-background-toggle"><input type="checkbox" data-action="set-book-background" data-book-id="${escapeHtml(bookId(book))}" ${effectiveBackground(book, summary) ? "checked" : ""} ${processIsActive() || state.bookInteriorSavePending ? "disabled" : ""}> Use Brand background</label></section>${renderIntroTemplateWorkspace(book, summary)}</section>`
      : state.selectedBookTab === "artwork"
        ? renderFolderAssetWorkspace(book, summary)
        : state.selectedBookTab === "pages"
          ? renderProcessedInteriorPages(summary)
        : `<section class="book-overview">${migrationWarning}<div class="summary-grid"><div><span>Status</span>${badge(workspaceStatus(summary))}</div><div><span>Interior preflight</span>${badge(valueFor(summary, "validationStatus", "Checking"))}</div><div><span>Last run</span><strong>${dateTime(valueFor(summary, "lastRunAt", null))}</strong></div><div><span>Pages (interior)</span><strong>${valueFor(summary, "interiorSourcePageCount", 0)}</strong></div></div>${renderBookInformation(book, summary)}${renderBookBrandAssignment(book, summary)}${renderBrandTemplateCopyCard(book, summary)}<p class="panel-note">Review the summary, then configure Brand background and Intro pages in Interior settings.</p></section>`;
    return `<div class="book-heading"><div><h2>${escapeHtml(bookDisplayTitle(book, summary))}</h2><p>Folder: ${escapeHtml(valueFor(book, "name", bookId(book)))}</p></div><div class="page-actions"><button class="button-secondary" data-action="validate-book" data-book-id="${escapeHtml(bookId(book))}">Run Interior preflight</button><button class="button-primary" data-action="queue-selected-book" ${readiness.ready ? "" : "disabled"} title="${escapeHtml(readiness.reason)}" aria-label="Process Interior. ${escapeHtml(readiness.reason)}">Process Interior</button></div></div><nav class="detail-tabs" role="tablist" aria-label="Book detail sections">${tabButton("overview", "Overview")}${tabButton("production", "Production")}${tabButton("settings", "Interior settings")}${tabButton("artwork", "Interior artwork")}${tabButton("pages", "Interior pages")}</nav><div id="book-panel-${state.selectedBookTab}" class="tab-body ${state.selectedBookTab === "artwork" ? "tab-body-artwork" : state.selectedBookTab === "pages" ? "tab-body-processed-pages" : ""}" role="tabpanel" aria-labelledby="book-tab-${state.selectedBookTab}" tabindex="0">${body}</div>`;
  };

  const renderIntroTemplateWorkspace = (book, summary) => {
    const assignment = assignmentReadiness(summary);
    const brand = assignment.ready ? assignment.brand : null;
    const allTemplates = valueFor(brand, "introTemplateAssets", []) ?? [];
    const templates = allTemplates.filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", "")));
    const selection = effectiveIntro(book, summary);
    const candidates = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    const byReference = new Map(candidates.map((asset) => [String(valueFor(asset, "sourceReference", "")).toLowerCase(), asset]));
    const selected = selection.sourceReferences.map((reference) => byReference.get(String(reference).toLowerCase())).filter(Boolean);
    const disabled = processIsActive() || state.bookInteriorSavePending;
    const readiness = introReadiness(book, summary);
    const selectedTile = (asset, index) => {
      const reference = valueFor(asset, "sourceReference", "");
      return `<article class="intro-template-tile"><span class="intro-template-preview">${localImageMarkup(asset, `Custom Intro ${valueFor(asset, "fileName", "")}`, "Image unavailable")}</span><strong>Intro #${index + 1}</strong><span title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</span><div class="intro-template-actions"><button class="button-secondary" data-action="intro-move-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-index="${index}" data-intro-direction="up" aria-label="Move ${escapeHtml(valueFor(asset, "fileName", ""))} earlier" ${index === 0 || disabled ? "disabled" : ""}>Earlier</button><button class="button-secondary" data-action="intro-move-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-index="${index}" data-intro-direction="down" aria-label="Move ${escapeHtml(valueFor(asset, "fileName", ""))} later" ${index === selected.length - 1 || disabled ? "disabled" : ""}>Later</button><button class="button-secondary" data-action="intro-remove-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-source-reference="${escapeHtml(reference)}" ${disabled ? "disabled" : ""}>Remove</button></div></article>`;
    };
    const availableOption = (asset) => `<button class="intro-template-add" data-action="intro-add-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-source-reference="${escapeHtml(valueFor(asset, "sourceReference", ""))}" ${disabled ? "disabled" : ""}>${localImageMarkup(asset, `Available Book interior page ${valueFor(asset, "fileName", "")}`, "Image unavailable")}<span>${escapeHtml(valueFor(asset, "fileName", ""))}</span><small>Add as Intro</small></button>`;
    const brandCopy = brand ? `${escapeHtml(valueFor(brand, "name", ""))} · ${templates.length} eligible local template${templates.length === 1 ? "" : "s"}` : escapeHtml(assignment.reason);
    const allItems = selection.hasIntro ? candidates : templates;
    const introPageSize = 6;
    const totalPages = Math.max(1, Math.ceil(allItems.length / introPageSize));
    state.introTemplatePage = Math.min(totalPages, Math.max(1, state.introTemplatePage));
    const start = (state.introTemplatePage - 1) * introPageSize;
    const pageItems = allItems.slice(start, start + introPageSize);
    const visibleItems = selection.hasIntro
      ? pageItems.map((asset) => {
        const index = selection.sourceReferences.findIndex((reference) => String(reference).toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase());
        return index >= 0 ? selectedTile(asset, index) : availableOption(asset);
      }).join("")
      : pageItems.map((asset) => `<article class="intro-template-tile"><span class="intro-template-preview">${introTemplateImageMarkup(asset, `Automatic Intro template ${valueFor(asset, "fileName", "")}`, valueFor(brand, "name", ""))}</span><strong>Automatic</strong><span title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</span></article>`).join("");
    const paging = `<footer class="intro-template-pagination" data-intro-total-pages="${totalPages}"><span>${allItems.length ? `${start + 1}–${Math.min(start + introPageSize, allItems.length)} of ${allItems.length}` : "0 pages"}</span><div><button class="button-secondary" data-action="intro-template-page" data-intro-template-page="previous" ${state.introTemplatePage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.introTemplatePage} of ${totalPages}</span><button class="button-secondary" data-action="intro-template-page" data-intro-template-page="next" ${state.introTemplatePage === totalPages ? "disabled" : ""}>Next</button></div></footer>`;
    const sourceCopy = selection.hasIntro
      ? `${selected.length ? `${selected.length} selected Intro page${selected.length === 1 ? "" : "s"}. Use each card to preserve order or remove it.` : "Select at least one Book interior page to make this Book ready."}`
      : `All eligible templates are added in filename order. ${finalInteriorPageSize().width} × ${finalInteriorPageSize().height} Brand artwork is assembled directly into the PDF; 1024 × 1024 and 2048 × 2048 templates retain the legacy processing path. Book interior pages remain eligible for normal Interior processing.`;
    return `<section class="intro-template-workspace"><div class="intro-template-heading"><div><h3>Intro pages</h3><p>${selection.hasIntro ? "Choose ordered pages from this Book's Book interior." : brandCopy}</p></div><span class="status-badge ${selection.hasIntro ? "status-warn" : "status-muted"}">${selection.hasIntro ? "Custom Book interior" : "Automatic Brand template"}</span></div><p class="${readiness.ready ? "panel-note" : "intro-template-warning"}" role="${readiness.ready ? "status" : "alert"}">${readiness.ready ? "Ready for backend size validation during processing." : escapeHtml(readiness.reason)}</p><fieldset class="intro-mode-choice" ${disabled ? "disabled" : ""}><legend>Intro source</legend><label><input type="radio" name="intro-mode" data-action="set-intro-mode" data-book-id="${escapeHtml(bookId(book))}" value="auto" ${selection.hasIntro ? "" : "checked"}> Automatic <small>Use every eligible current Brand IntroTemplate in filename order.</small></label><label><input type="radio" name="intro-mode" data-action="set-intro-mode" data-book-id="${escapeHtml(bookId(book))}" value="custom" ${selection.hasIntro ? "checked" : ""}> Custom <small>Choose Book interior pages and their print order.</small></label></fieldset><div class="intro-template-selection"><div><h4>${selection.hasIntro ? "Book interior pages" : "Automatic Brand IntroTemplate"}</h4><p>${sourceCopy}</p></div><div class="intro-template-page-grid">${visibleItems || "<p class=\"empty-copy\">No eligible Intro pages are available.</p>"}</div>${paging}</div></section>`;
  };

  const refreshIntroTemplateWorkspace = (focusPageAction = "") => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const workspace = document.querySelector(".intro-template-workspace");
    if (!book || !summary || !workspace) { render("books", false); return; }
    workspace.outerHTML = renderIntroTemplateWorkspace(book, summary);
    updateInteriorSaveUi();
    const oppositeAction = focusPageAction === "next" ? "previous" : "next";
    document.querySelector(`[data-action="intro-template-page"][data-intro-template-page="${oppositeAction}"]`)?.focus();
  };

  const renderFolderAssetWorkspace = (book, summary) => {
    const intro = effectiveIntro(book, summary);
    const isCustomIntro = (asset) => intro.hasIntro && intro.sourceReferences.some((item) => item.toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase());
    const allAssets = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior" && !isCustomIntro(asset));
    const sourceFolderNames = valueFor(summary, "sourceFolders", []).map((folder) => valueFor(folder, "name", "")).filter(Boolean);
    const folderFor = (asset) => sourceFolderNames.find((name) => valueFor(asset, "relativePath", "").replaceAll("\\", "/").toLowerCase().startsWith(`${name.toLowerCase()}/`)) ?? valueFor(asset, "folder", "Other");
    const matchesStatus = (asset) => !state.assetStatus || (state.assetStatus === "Active" ? effectiveInteriorAsset(book, asset).isActive : !effectiveInteriorAsset(book, asset).isActive);
    const matchesFrameMode = (asset) => !state.assetFrameMode || effectiveInteriorAsset(book, asset).frameMode === state.assetFrameMode;
    const matching = allAssets.filter((asset) => `${valueFor(asset, "fileName", "")} ${valueFor(asset, "relativePath", "")}`.toLowerCase().includes(state.assetFilter.toLowerCase()) && matchesStatus(asset) && matchesFrameMode(asset));
    const eligibleMatching = matching;
    const eligibleReferences = new Set(allAssets.map((asset) => String(valueFor(asset, "sourceReference", ""))));
    state.selectedArtworkReferences = new Set([...state.selectedArtworkReferences].filter((reference) => eligibleReferences.has(reference)));
    const selectedVisibleCount = eligibleMatching.filter((asset) => state.selectedArtworkReferences.has(String(valueFor(asset, "sourceReference", "")))).length;
    const selectedCount = state.selectedArtworkReferences.size;
    const tile = (asset) => {
      const settings = effectiveInteriorAsset(book, asset);
      const mode = settings.frameMode;
      const reference = valueFor(asset, "sourceReference", "");
      const active = settings.isActive;
      const selected = state.selectedArtworkReferences.has(String(reference));
      const disabled = processIsActive() || state.bookInteriorSavePending ? "disabled" : "";
      const statusBadge = active ? `<span class="status-badge status-good">Active</span>` : `<span class="status-badge status-bad">Inactive</span>`;
      const frameBadge = `<span class="artwork-frame-badge artwork-frame-${escapeHtml(mode)}">${mode === "enabled" ? "Frame" : "No Frame"}</span>`;
      const cardClass = `interior-artwork-card ${active ? "is-active" : "is-inactive"} ${selected ? "is-selected" : ""}`;
      const cardContent = `<div class="interior-artwork-preview"><span class="artwork-card-selection-indicator">${selected ? "Selected" : "Select"}</span>${localImageMarkup(asset, `Preview of ${valueFor(asset, "fileName", "asset")}`, "Image unavailable")}</div><div class="interior-artwork-copy"><strong title="${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}">${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}</strong><small title="${escapeHtml(folderFor(asset))}">${escapeHtml(folderFor(asset))} · ${escapeHtml(assetDimensions(asset))}</small><div class="interior-artwork-badges">${statusBadge}${frameBadge}</div></div>`;
      return `<button type="button" class="${cardClass}" data-action="toggle-artwork-selection" data-source-reference="${escapeHtml(reference)}" aria-pressed="${selected}" aria-label="${selected ? "Deselect" : "Select"} ${escapeHtml(valueFor(asset, "fileName", "artwork"))}; ${active ? "Active" : "Inactive"}; ${mode === "enabled" ? "Frame" : "No Frame"}" ${disabled}>${cardContent}</button>`;
    };
    const activeCount = allAssets.filter((asset) => effectiveInteriorAsset(book, asset).isActive).length;
    const inactiveCount = allAssets.length - activeCount;
    const controlsDisabled = processIsActive() || state.bookInteriorSavePending;
    const allShownSelected = eligibleMatching.length > 0 && selectedVisibleCount === eligibleMatching.length;
    const bulkDisabled = !selectedCount || (state.assetBulkActive === "unchanged" && state.assetBulkFrameMode === "unchanged") || controlsDisabled;
    return `<section class="interior-artwork-workspace"><header class="interior-artwork-heading"><div><h3>Interior artwork</h3><p>Review every available Book interior page, then control whether it is processed and which frame mode it uses.</p></div><p class="interior-artwork-count" role="status"><strong>${allAssets.length}</strong> artwork · <strong>${activeCount}</strong> active · <strong>${inactiveCount}</strong> inactive</p></header><div class="interior-artwork-filters"><label class="field asset-search-field"><span>Search artwork</span><input class="control" data-action="filter-assets" value="${escapeHtml(state.assetFilter)}" placeholder="File name or folder"></label><section class="asset-filter-controls" aria-label="Filter artwork"><span class="asset-filter-label">Filter artwork</span><div class="asset-filter-chip-groups"><div class="asset-status-filter" role="group" aria-label="Interior artwork status filters">${["Active", "Inactive"].map((name) => `<button type="button" class="${state.assetStatus === name ? "active" : ""}" data-action="asset-status" data-asset-status="${name}" aria-pressed="${state.assetStatus === name}">${name}</button>`).join("")}</div><div class="asset-frame-filter" role="group" aria-label="Interior artwork frame mode filters">${[["", "All"], ["enabled", "Frame"], ["disabled", "No Frame"]].map(([value, label]) => `<button type="button" class="${state.assetFrameMode === value ? "active" : ""}" data-action="asset-frame-mode" data-asset-frame-mode="${value}" aria-pressed="${state.assetFrameMode === value}">${label}</button>`).join("")}</div></div></section></div><div class="interior-artwork-bulk-toolbar"><label class="artwork-select-all"><input type="checkbox" data-action="toggle-all-artwork" ${allShownSelected ? "checked" : ""} ${!eligibleMatching.length || controlsDisabled ? "disabled" : ""}> Select all ${eligibleMatching.length} shown</label><label class="field artwork-bulk-field"><span>Status</span><select class="control h-8" data-action="set-artwork-bulk-active"><option value="unchanged" ${state.assetBulkActive === "unchanged" ? "selected" : ""}>No change</option><option value="active" ${state.assetBulkActive === "active" ? "selected" : ""}>Active</option><option value="inactive" ${state.assetBulkActive === "inactive" ? "selected" : ""}>Inactive</option></select></label><label class="field artwork-bulk-field"><span>Frame mode</span><select class="control h-8" data-action="set-artwork-bulk-frame-mode"><option value="unchanged" ${state.assetBulkFrameMode === "unchanged" ? "selected" : ""}>No change</option><option value="enabled" ${state.assetBulkFrameMode === "enabled" ? "selected" : ""}>Frame</option><option value="disabled" ${state.assetBulkFrameMode === "disabled" ? "selected" : ""}>No Frame</option></select></label><button class="button-primary" data-action="apply-artwork-bulk" ${bulkDisabled ? "disabled" : ""}>Apply to ${selectedCount} selected</button></div><p class="asset-result-count" role="status" aria-atomic="true">${matching.length} artwork shown</p><div class="interior-artwork-grid-scroll"><div class="interior-artwork-grid">${matching.length ? matching.map(tile).join("") : `<p class="empty-copy interior-artwork-empty">No artwork matches this view. <button type="button" class="button-link" data-action="clear-artwork-filters">Clear filters</button></p>`}</div></div></section>`;
  };

  const refreshInteriorArtworkWorkspace = () => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const workspace = document.querySelector(".interior-artwork-workspace");
    if (!book || !summary || !workspace) { render("books", false); return; }
    const grid = workspace.querySelector?.(".interior-artwork-grid-scroll") ?? document.querySelector(".interior-artwork-grid-scroll");
    if (grid && Number.isFinite(grid.scrollTop)) state.artworkGridScrollTop = grid.scrollTop;
    const activeElement = document.activeElement;
    const restoreSearch = activeElement?.dataset?.action === "filter-assets";
    const caret = activeElement?.selectionStart ?? state.assetFilter.length;
    workspace.outerHTML = renderFolderAssetWorkspace(book, summary);
    const refreshedGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (refreshedGrid) refreshedGrid.scrollTop = state.artworkGridScrollTop;
    if (restoreSearch) {
      const search = document.querySelector('[data-action="filter-assets"]');
      search?.focus();
      search?.setSelectionRange(caret, caret);
    }
    updateInteriorSaveUi();
  };

  const refreshBookDrawerBody = (focusTab = "") => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const drawerBody = document.querySelector(".book-drawer-body");
    if (!book || !summary || !drawerBody) { render("books", false); return; }
    if (Number.isFinite(drawerBody.scrollTop)) state.bookDrawerScrollTop = drawerBody.scrollTop;
    const artworkGrid = drawerBody.querySelector?.(".interior-artwork-grid-scroll") ?? document.querySelector(".interior-artwork-grid-scroll");
    if (artworkGrid && Number.isFinite(artworkGrid.scrollTop)) state.artworkGridScrollTop = artworkGrid.scrollTop;
    drawerBody.innerHTML = renderBookTabs(book, summary);
    if (Number.isFinite(state.bookDrawerScrollTop)) drawerBody.scrollTop = state.bookDrawerScrollTop;
    const refreshedGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (refreshedGrid && Number.isFinite(state.artworkGridScrollTop)) refreshedGrid.scrollTop = state.artworkGridScrollTop;
    updateInteriorSaveUi();
    if (focusTab) document.querySelector(`[data-action="book-tab"][data-book-tab="${focusTab}"]`)?.focus();
  };

  const renderBookDrawer = (book, summary) => {
    if (!state.bookDrawerOpen || !book || !summary) return "";
    const cover = assetForReference(summary, valueFor(summary, "representativeCoverReference", ""));
    const dirty = hasInteriorDraft(bookId(book));
    const saveDisabled = !workspaceStateAvailable(summary) || !dirty || processIsActive() || state.bookInteriorSavePending;
    const metadataDirty = hasMetadataDraft(book, summary);
    return `<div class="book-drawer-layer"><section class="book-drawer" role="dialog" aria-labelledby="book-drawer-title"><header class="book-drawer-header"><span class="book-drawer-preview">${localImageMarkup(cover, `Cover for ${bookDisplayTitle(book, summary)}`)}</span><div><p class="eyebrow">Book detail</p><h2 id="book-drawer-title" tabindex="-1">${escapeHtml(bookDisplayTitle(book, summary))}</h2><p class="book-folder-name">${escapeHtml(valueFor(book, "name", bookId(book)))}</p><div>${badge(productionStatus(summary, book))} ${badge(bookFrameState(summary))} <span data-book-assigned-brand-badge>${badge(assignedBrandName(summary) || "Unassigned")}</span></div></div><div class="book-drawer-actions"><span data-book-interior-unsaved role="status" ${dirty || metadataDirty ? "" : "hidden"}>Unsaved changes</span><button class="button-primary" data-action="save-book-interior-settings" data-book-id="${escapeHtml(bookId(book))}" ${saveDisabled ? "disabled" : ""} aria-busy="${state.bookInteriorSavePending}">${state.bookInteriorSavePending ? "Saving…" : "Save Interior changes"}</button><button class="button-secondary" data-action="close-book-drawer" aria-label="Close Book detail">Close</button></div></header><div class="book-drawer-body">${renderBookTabs(book, summary)}</div></section></div>`;
  };

  const updateBookCatalogMutationUi = () => {
    if (!state.bookDrawerOpen || currentRoute() !== "books") return;
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary) return;
    const busy = catalogMutationBusy() || processIsActive();
    const metadataCard = document.querySelector("[data-book-information-card]");
    const metadataSave = metadataCard?.querySelector('[data-action="save-book-metadata"]');
    const metadataError = subcoverError(metadataDraftFor(book, summary).subcover);
    metadataCard?.querySelectorAll("input, textarea").forEach((control) => { control.disabled = busy; });
    if (metadataSave) {
      metadataSave.disabled = busy || !hasMetadataDraft(book, summary) || Boolean(metadataError);
      metadataSave.textContent = state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save" ? "Saving…" : "Save Book Information";
    }
    const metadataFeedback = metadataCard?.querySelector('[data-catalog-feedback="metadata"]');
    if (metadataFeedback) {
      const visible = state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.metadata.save";
      metadataFeedback.textContent = visible ? state.catalogFeedback : "";
      metadataFeedback.classList.toggle("is-error", visible && state.catalogFeedbackError);
      metadataFeedback.setAttribute("role", visible && state.catalogFeedbackError ? "alert" : "status");
    }
    const assignmentCard = document.querySelector("[data-book-assignment-card]");
    const select = assignmentCard?.querySelector('[data-action="book-brand-select"]');
    if (select) select.disabled = busy || !valueFor(metadataFor(summary), "author", "");
    const assign = assignmentCard?.querySelector('[data-action="assign-book-brand"]');
    if (assign) {
      assign.disabled = busy || !select?.value || select.value === assignedBrandName(summary);
      assign.textContent = state.catalogMutationPending && state.catalogMutationCommand === "book.brand.assign" ? "Assigning…" : "Assign Brand";
    }
    const unassign = assignmentCard?.querySelector('[data-action="unassign-book-brand"]');
    if (unassign) unassign.disabled = busy || !assignedBrandName(summary);
    const assignmentFeedback = assignmentCard?.querySelector('[data-catalog-feedback="assignment"]');
    if (assignmentFeedback) {
      const visible = state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand !== "book.metadata.save";
      assignmentFeedback.textContent = visible ? state.catalogFeedback : "";
      assignmentFeedback.classList.toggle("is-error", visible && state.catalogFeedbackError);
      assignmentFeedback.setAttribute("role", visible && state.catalogFeedbackError ? "alert" : "status");
    }
  };

  const refreshBookCatalogCards = (command) => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary || !state.bookDrawerOpen || state.selectedBookTab !== "overview") return;
    const drawerBody = document.querySelector(".book-drawer-body");
    const scrollTop = drawerBody?.scrollTop ?? state.bookDrawerScrollTop;
    const syncAssignmentCard = () => {
      const card = document.querySelector("[data-book-assignment-card]");
      if (!card) return;
      const template = document.createElement("template");
      template.innerHTML = renderBookBrandAssignment(book, summary);
      const next = template.content.firstElementChild;
      const badgeCurrent = card.querySelector(".catalog-card-heading > .status-badge");
      const badgeNext = next.querySelector(".catalog-card-heading > .status-badge");
      if (badgeCurrent && badgeNext) { badgeCurrent.className = badgeNext.className; badgeCurrent.textContent = badgeNext.textContent; }
      const summaryCurrent = card.querySelectorAll(".catalog-assignment-summary dd");
      const summaryNext = next.querySelectorAll(".catalog-assignment-summary dd");
      summaryCurrent.forEach((item, index) => { item.textContent = summaryNext[index]?.textContent ?? ""; });
      const warningCurrent = card.querySelector(":scope > .catalog-warning");
      const warningNext = next.querySelector(":scope > .catalog-warning");
      if (warningCurrent && warningNext) warningCurrent.textContent = warningNext.textContent;
      else if (warningCurrent) warningCurrent.remove();
      else if (warningNext) card.querySelector(":scope > .field")?.before(warningNext.cloneNode(true));
      const selectCurrent = card.querySelector('[data-action="book-brand-select"]');
      const selectNext = next.querySelector('[data-action="book-brand-select"]');
      if (selectCurrent && selectNext) { selectCurrent.innerHTML = selectNext.innerHTML; selectCurrent.disabled = selectNext.disabled; }
      const helpCurrent = card.querySelector(":scope > .field small");
      const helpNext = next.querySelector(":scope > .field small");
      if (helpCurrent && helpNext) helpCurrent.textContent = helpNext.textContent;
    };
    if (command === "book.metadata.save") {
      const metadataCard = document.querySelector("[data-book-information-card]");
      const persisted = metadataValues(summary);
      metadataCard?.querySelectorAll("[data-metadata-field]").forEach((control) => { control.value = persisted[control.dataset.metadataField] ?? ""; });
      metadataCard?.querySelector(".catalog-card-heading > .status-badge")?.remove();
      syncAssignmentCard();
      const title = document.getElementById("book-drawer-title");
      if (title) title.textContent = bookDisplayTitle(book, summary);
    } else {
      syncAssignmentCard();
    }
    if (drawerBody) drawerBody.scrollTop = scrollTop;
    updateBookCatalogMutationUi();
  };

  const refreshBrandDependentBookUi = (command) => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary || !state.bookDrawerOpen) return;
    const drawerBody = document.querySelector(".book-drawer-body");
    const scrollTop = drawerBody?.scrollTop ?? state.bookDrawerScrollTop;
    const readiness = processingReadiness(book, summary);
    const processButton = document.querySelector('[data-action="queue-selected-book"]');
    if (processButton) {
      processButton.disabled = !readiness.ready;
      processButton.title = readiness.reason;
      processButton.setAttribute("aria-label", `Process Interior. ${readiness.reason}`);
    }
    const assignedBadge = document.querySelector("[data-book-assigned-brand-badge]");
    if (assignedBadge) assignedBadge.innerHTML = badge(assignedBrandName(summary) || "Unassigned");

    if (state.selectedBookTab === "overview") {
      refreshBookCatalogCards(command);
      const copyCard = document.querySelector("[data-brand-template-copy-card]");
      if (copyCard) copyCard.outerHTML = renderBrandTemplateCopyCard(book, summary);
    } else if (state.selectedBookTab === "settings") {
      refreshIntroTemplateWorkspace();
    } else if (state.selectedBookTab === "production") {
      refreshProductionWorkspace();
    }
    if (drawerBody) drawerBody.scrollTop = scrollTop;
  };

  const matchesBookBrandFilter = (summary) => state.bookBrandFilter === "All" || (state.bookBrandFilter === "Unassigned" ? !assignedBrandName(summary) : assignedBrandName(summary) === state.bookBrandFilter);
  const filteredBooks = () => books().filter((book) => {
    const summary = summaryFor(book);
    const metadata = metadataFor(summary);
    const searchable = `${bookDisplayTitle(book, summary)} ${valueFor(book, "name", "")} ${valueFor(metadata, "author", "")}`.toLocaleLowerCase();
    return searchable.includes(state.bookFilter.toLocaleLowerCase()) && matchesBookBrandFilter(summary) &&
      (state.bookStatus === "All" || productionStatus(summary, book) === state.bookStatus);
  }).sort((left, right) => {
    const leftSummary = summaryFor(left);
    const rightSummary = summaryFor(right);
    if (state.bookSort === "name") return bookDisplayTitle(left, leftSummary).localeCompare(bookDisplayTitle(right, rightSummary));
    return new Date(valueFor(rightSummary, "lastRunAt", 0)).getTime() - new Date(valueFor(leftSummary, "lastRunAt", 0)).getTime();
  });

  const refreshBookSelectionUi = () => {
    const filtered = filteredBooks();
    const pageItems = filtered.slice((state.bookPage - 1) * 12, state.bookPage * 12);
    const selectedCount = state.selectedBookIds.size;
    const selectablePageItems = pageItems.filter((book) => workspaceStateAvailable(summaryFor(book)));
    const selectableFiltered = filtered.filter((book) => workspaceStateAvailable(summaryFor(book)));
    const pageSelectedCount = selectablePageItems.filter((book) => state.selectedBookIds.has(bookId(book))).length;
    const allPageSelected = selectablePageItems.length > 0 && pageSelectedCount === selectablePageItems.length;
    const allFilteredSelected = selectableFiltered.length > 0 && selectableFiltered.every((book) => state.selectedBookIds.has(bookId(book)));

    document.querySelectorAll("[data-book-card-id]").forEach((card) => {
      const id = card.dataset.bookCardId;
      const book = books().find((item) => bookId(item) === id);
      const selected = state.selectedBookIds.has(id);
      const name = valueFor(book, "name", id);
      card.classList.toggle("selected", selected);
      const toggle = card.querySelector('[data-action="toggle-book-selection"]');
      if (toggle) {
        toggle.disabled = !workspaceStateAvailable(summaryFor(book));
        toggle.setAttribute("aria-pressed", String(selected));
        toggle.setAttribute("aria-label", `${selected ? "Remove" : "Select"} ${name} ${selected ? "from" : "for"} Interior Processing`);
      }
      const icon = card.querySelector("[data-book-selection-icon]");
      if (icon) icon.innerHTML = bookSelectIcon(selected);
    });

    const process = document.querySelector('[data-action="go-process"]');
    if (process) process.textContent = selectedCount ? `Process Interior · ${selectedCount} selected` : "Process Interior";
    const count = document.querySelector("[data-book-selection-count]");
    if (count) count.textContent = `${selectedCount} selected`;
    const pageSelection = document.querySelector('[data-action="toggle-book-page-selection"]');
    if (pageSelection) {
      pageSelection.checked = allPageSelected;
      pageSelection.indeterminate = pageSelectedCount > 0 && !allPageSelected;
    }
    const selectAll = document.querySelector('[data-action="select-all-filtered-books"]');
    if (selectAll) selectAll.disabled = !selectableFiltered.length || allFilteredSelected;
    const clear = document.querySelector('[data-action="clear-book-selection"]');
    if (clear) clear.disabled = !selectedCount;
  };

  const renderBooks = () => {
    const existingDrawerBody = document.querySelector(".book-drawer-body");
    if (existingDrawerBody && Number.isFinite(existingDrawerBody.scrollTop)) state.bookDrawerScrollTop = existingDrawerBody.scrollTop;
    const existingArtworkGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (existingArtworkGrid && Number.isFinite(existingArtworkGrid.scrollTop)) state.artworkGridScrollTop = existingArtworkGrid.scrollTop;
    const activeElement = document.activeElement;
    if (activeElement?.dataset.action === "filter-assets") {
      state.assetSearchFocused = true;
      state.assetSearchCaret = activeElement.selectionStart ?? activeElement.value.length;
    }
    const allBooks = books();
    const brandScopedBooks = allBooks.filter((book) => matchesBookBrandFilter(summaryFor(book)));
    const statusCounts = bookStatuses.map((name) => ({ name, count: name === "All" ? brandScopedBooks.length : brandScopedBooks.filter((book) => productionStatus(summaryFor(book), book) === name).length }));
    const filtered = filteredBooks();
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
      const selected = state.selectedBookIds.has(id);
      const stateAvailable = workspaceStateAvailable(itemSummary);
      const name = bookDisplayTitle(item, itemSummary);
      const folder = valueFor(item, "name", id);
      const assignment = assignedBrandName(itemSummary);
      const assignmentBadges = assignmentStatus(itemSummary) === "Valid" ? badge(assignment) : assignment ? `${badge(assignment)} ${badge(assignmentLabel(itemSummary))}` : badge("Unassigned");
      return `<article class="book-card ${selected ? "selected" : ""} ${stateAvailable ? "" : "is-disabled"}" data-book-card-id="${escapeHtml(id)}"><button type="button" class="book-card-main" data-action="toggle-book-selection" data-book-id="${escapeHtml(id)}" aria-label="${stateAvailable ? `${selected ? "Remove" : "Select"} ${escapeHtml(name)} ${selected ? "from" : "for"} Interior Processing` : `${escapeHtml(name)} cannot be selected because its workspace state is unavailable`}" aria-pressed="${selected}" ${stateAvailable ? "" : "disabled"}><span class="book-card-preview"><span class="book-card-selection-icon" data-book-selection-icon aria-hidden="true">${bookSelectIcon(selected)}</span>${thumbnail}</span><span class="book-card-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><small title="${escapeHtml(folder)}">${escapeHtml(folder)} · ${escapeHtml(valueFor(metadataFor(itemSummary), "author", "") || "Unknown Author")} · ${active} / ${total} Interior active</small><span>${badge(status)} ${assignmentBadges}${stateAvailable ? "" : ` ${badge("State unavailable")}`}</span></span></button><button type="button" class="book-card-edit" data-action="open-book-detail" data-book-id="${escapeHtml(id)}" aria-label="Open Book detail for ${escapeHtml(name)}" title="Open Book detail">${bookEditIcon()}</button></article>`;
    };
    const start = filtered.length ? (state.bookPage - 1) * pageSize + 1 : 0;
    const end = Math.min(state.bookPage * pageSize, filtered.length);
    const selectedCount = state.selectedBookIds.size;
    const selectablePageItems = pageItems.filter((item) => workspaceStateAvailable(summaryFor(item)));
    const pageSelectedCount = selectablePageItems.filter((item) => state.selectedBookIds.has(bookId(item))).length;
    const allPageSelected = selectablePageItems.length > 0 && pageSelectedCount === selectablePageItems.length;
    const processLabel = selectedCount ? `Process Interior · ${selectedCount} selected` : "Process Interior";
    const brandFilterOptions = [{ value: "All", label: "All", count: allBooks.length }, { value: "Unassigned", label: "Unassigned", count: allBooks.filter((book) => !assignedBrandName(summaryFor(book))).length }, ...[...brands()].sort((left, right) => valueFor(left, "name", "").localeCompare(valueFor(right, "name", ""), undefined, { sensitivity: "base" })).map((brand) => { const name = valueFor(brand, "name", ""); return { value: name, label: name, count: allBooks.filter((book) => assignedBrandName(summaryFor(book)) === name).length }; })];
    content.innerHTML = `<section class="book-library-page"><div class="page-header"><div><h1>Books</h1><p>Filter local Books by their explicit Brand assignment before continuing production.</p></div><div class="page-actions">${refreshAction()}<button class="button-secondary" data-action="clear-cache" ${cacheCleanupBlocked() ? "disabled" : ""}>${state.cacheCleanupActive ? "Clearing…" : "Clear Cache"}</button><button class="button-secondary" data-action="validate-all">Validate all</button><button class="button-primary" data-action="go-process">${processLabel}</button></div></div><section class="book-toolbar"><label class="book-selection-page book-toolbar-selection"><input type="checkbox" data-action="toggle-book-page-selection" ${allPageSelected ? "checked" : ""} ${selectablePageItems.length ? "" : "disabled"}> Select page <span>(${selectablePageItems.length})</span></label><label class="field book-search-field"><span>Search books</span><input class="control" data-action="filter-books" value="${escapeHtml(state.bookFilter)}" placeholder="Title, folder or Author"></label><label class="field"><span>Book Brand</span><select class="control" data-action="book-brand-filter">${brandFilterOptions.map(({ value, label, count }) => `<option value="${escapeHtml(value)}" ${state.bookBrandFilter === value ? "selected" : ""}>${escapeHtml(label)} (${count})</option>`).join("")}</select></label><label class="field"><span>Sort</span><select class="control" data-action="book-sort"><option value="activity" ${state.bookSort === "activity" ? "selected" : ""}>Last activity</option><option value="name" ${state.bookSort === "name" ? "selected" : ""}>Book title</option></select></label><label class="field book-status-filter"><span>Status</span><select class="control" data-action="book-status">${statusCounts.map(({ name, count }) => `<option value="${escapeHtml(name)}" ${state.bookStatus === name ? "selected" : ""}>${escapeHtml(name)} (${count})</option>`).join("")}</select></label><div class="asset-view-toggle" aria-label="Book view"><button class="${state.bookView === "grid" ? "active" : ""}" data-action="book-view" data-book-view="grid" aria-pressed="${state.bookView === "grid"}">Grid</button><button class="${state.bookView === "list" ? "active" : ""}" data-action="book-view" data-book-view="list" aria-pressed="${state.bookView === "list"}">Compact list</button></div></section><section class="book-library-results"><div class="book-library-grid-scroll"><section class="${state.bookView === "grid" ? "book-grid" : "book-compact-list"}">${pageItems.length ? pageItems.map(card).join("") : `<div class="book-grid-empty"><strong>No Books match this view.</strong><span>Adjust the search, Book Brand or status filter.</span></div>`}</section></div><footer class="book-pagination" data-book-total-pages="${totalPages}"><span>${start}–${end} of ${filtered.length}</span><div><button class="button-secondary" data-action="book-page" data-book-page="first" ${state.bookPage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="book-page" data-book-page="previous" ${state.bookPage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.bookPage} of ${totalPages}</span><button class="button-secondary" data-action="book-page" data-book-page="next" ${state.bookPage === totalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="book-page" data-book-page="last" ${state.bookPage === totalPages ? "disabled" : ""}>Last</button></div></footer></section></section>`;
    const pageSelection = document.querySelector('[data-action="toggle-book-page-selection"]');
    if (pageSelection) pageSelection.indeterminate = pageSelectedCount > 0 && !allPageSelected;
    const selected = selectedBook();
    if (selected) content.insertAdjacentHTML("beforeend", renderBookDrawer(selected, summaryFor(selected)));
    if (state.drawerFocusTitle) {
      state.drawerFocusTitle = false;
      document.getElementById("book-drawer-title")?.focus();
    }
    if (state.restoreBookFocus) {
      state.restoreBookFocus = false;
      document.querySelector(`[data-action="open-book-detail"][data-book-id="${CSS.escape(state.selectedBookId)}"]`)?.focus();
    }
    const drawerBody = document.querySelector(".book-drawer-body");
    if (drawerBody && Number.isFinite(state.bookDrawerScrollTop)) drawerBody.scrollTop = state.bookDrawerScrollTop;
    const artworkGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (artworkGrid && Number.isFinite(state.artworkGridScrollTop)) artworkGrid.scrollTop = state.artworkGridScrollTop;
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
    const queueLocked = active || cancelling;
    const sessionQueue = valueFor(session, "queue", []);
    const hasSession = active || cancelling || sessionQueue.length > 0;
    const terminal = hasSession && !active && !cancelling;
    const mode = hasSession ? processMode(session) : "interior-only";
    const productionSession = mode === "production-interior" || mode === "full-book";
    const sessionName = productionSession ? "Build Final Interior" : "Process Interior";
    const pendingQueue = [...state.selectedBookIds].map((id) => {
      const book = books().find((candidate) => bookId(candidate) === id);
      return { bookId: { value: id }, status: "Ready", detail: valueFor(book, "name", id) };
    });
    const selectionReadiness = selectedProcessingReadiness();
    const queue = queueLocked ? sessionQueue : pendingQueue;
    const queueCurrentBook = sessionQueue.find((entry) => ["Running", "Processing"].includes(displayStatus(valueFor(entry, "status", "")))) ?? sessionQueue[0];
    const currentBook = valueFor(valueFor(session, "currentBookId", {}), "value", valueFor(valueFor(queueCurrentBook, "bookId", {}), "value", terminal ? "Last session" : "No active Book"));
    const terminalStatuses = sessionQueue.map((entry) => displayStatus(valueFor(entry, "status", "Not started")));
    const derivedTerminalStep = terminalStatuses.includes("Failed")
      ? "Failed"
      : terminalStatuses.includes("Cancelled")
        ? "Cancelled"
        : "Completed";
    const rawCurrentStep = valueFor(session, "currentStep", null) || (terminal ? derivedTerminalStep : "Preparing");
    const currentStep = processStepLabel(mode, rawCurrentStep);
    const completed = valueFor(session, "pagesCompleted", 0);
    const total = valueFor(session, "pagesTotal", 0);
    const percent = total ? Math.min(100, Math.round((completed / total) * 100)) : 0;
    const stage = cancelling ? "Cancelling" : terminal ? derivedTerminalStep : currentStep;
    const stageModel = processStageModel(mode, rawCurrentStep, terminal ? derivedTerminalStep : "");
    const stages = stageModel.stages;
    const currentStageIndex = stageModel.index;
    const failureDetails = sessionQueue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Failed" && valueFor(entry, "detail", null));
    const outcomeCopy = terminal
      ? derivedTerminalStep === "Completed"
        ? productionSession ? "Final Interior built successfully." : "Interior pages prepared. Existing PDF unchanged."
        : derivedTerminalStep === "Cancelled"
          ? productionSession ? "Final Interior build cancelled. Previous PDF kept." : "Processing cancelled. Processed previews were cleared; existing PDF was kept."
          : productionSession ? "Final Interior build failed. Previous PDF kept." : "Processing failed. Processed previews were cleared; existing PDF was kept."
      : "";
    const outcomeMarkup = outcomeCopy
      ? `<div class="process-session-outcome ${derivedTerminalStep === "Failed" ? "process-failure" : ""}" role="${derivedTerminalStep === "Failed" ? "alert" : "status"}" ${derivedTerminalStep === "Failed" ? 'tabindex="-1" data-process-failure-summary' : ""}><strong>${escapeHtml(outcomeCopy)}</strong></div>`
      : "";
    const queueTotalPages = Math.max(1, Math.ceil(queue.length / processQueuePageSize));
    state.processQueuePage = Math.min(Math.max(1, state.processQueuePage), queueTotalPages);
    const queueStart = (state.processQueuePage - 1) * processQueuePageSize;
    const queueItems = queue.slice(queueStart, queueStart + processQueuePageSize);
    const queueRangeStart = queue.length ? queueStart + 1 : 0;
    const queueRangeEnd = Math.min(queueStart + processQueuePageSize, queue.length);
    const completedBooks = sessionQueue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Completed").length;
    const failedBooks = sessionQueue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Failed").length;
    const renderQueueCard = (entry) => {
      const id = valueFor(valueFor(entry, "bookId", {}), "value", "");
      const book = books().find((candidate) => bookId(candidate) === id);
      const summary = book ? summaryFor(book) : null;
      const name = valueFor(book, "name", valueFor(entry, "detail", id));
      const totalPages = valueFor(summary, "interiorSourcePageCount", 0);
      const activePages = valueFor(summary, "activeInteriorSourcePageCount", totalPages);
      const entryStatus = queueLocked ? valueFor(entry, "status", "NotStarted") : "Ready";
      const details = totalPages ? `${activePages} / ${totalPages} Interior active` : queueLocked ? valueFor(entry, "detail", "Waiting") : "Ready to prepare Interior pages";
      const preview = book ? bookThumbnailMarkup(book, summary, "Cover unavailable") : `<span class="book-preview-fallback">Cover unavailable</span>`;
      const main = book
        ? `<button type="button" class="process-queue-card-main" data-action="select-book" data-book-id="${escapeHtml(id)}" aria-label="Open ${escapeHtml(name)}"><span class="process-queue-preview">${preview}</span><span class="process-queue-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><small>${escapeHtml(details)}</small><span>${badge(entryStatus)}</span></span></button>`
        : `<div class="process-queue-card-main"><span class="process-queue-preview">${preview}</span><span class="process-queue-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><small>${escapeHtml(details)}</small><span>${badge(entryStatus)}</span></span></div>`;
      const action = queueLocked
        ? `<span class="process-queue-locked">Queue locked</span>`
        : `<button class="button-secondary process-queue-remove" data-action="remove-process-queue-book" data-book-id="${escapeHtml(id)}" aria-label="Remove ${escapeHtml(name)} from selected queue">Remove</button>`;
      return `<article class="process-queue-card">${main}<footer>${action}</footer></article>`;
    };
    const queueTab = `<section class="process-queue-workspace" aria-labelledby="selected-queue-title"><header class="process-queue-heading"><div><h2 id="selected-queue-title">Selected queue <span>${queue.length}</span></h2><p>${queueLocked ? `Queue is locked while ${sessionName} is running.` : "Review selected Books before preparing Interior pages."}</p></div><span class="process-queue-range" aria-live="polite">${queueRangeStart}–${queueRangeEnd} of ${queue.length}</span></header><div class="process-queue-grid-scroll"><div class="process-queue-grid">${queueItems.length ? queueItems.map(renderQueueCard).join("") : `<div class="process-queue-empty"><strong>No Books selected</strong><span>Select ready Books from the Books workspace, then return here to process them.</span><button class="button-secondary" data-action="go-books">Go to Books</button></div>`}</div></div><footer class="process-queue-pagination" data-process-queue-total-pages="${queueTotalPages}"><span>${queueRangeStart}–${queueRangeEnd} of ${queue.length}</span><div><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="first" ${state.processQueuePage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="previous" ${state.processQueuePage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.processQueuePage} of ${queueTotalPages}</span><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="next" ${state.processQueuePage === queueTotalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="last" ${state.processQueuePage === queueTotalPages ? "disabled" : ""}>Last</button></div></footer></section>`;
    const resolvedQueueBrand = queueLocked ? valueFor(session, "brandName", "Resolving") || "Resolving" : selectionReadiness.brandName || "—";
    const overviewTab = `<section class="process-overview-grid"><section class="panel process-summary-panel"><div class="process-panel-heading"><div><h2 class="panel-title">Summary</h2><p>${terminal ? `Last ${sessionName} session` : queueLocked ? `Current ${sessionName} session` : "Books ready to process"} · Assigned Brand: ${escapeHtml(resolvedQueueBrand)}</p></div>${badge(stage)}</div><div class="process-summary-stats" aria-live="polite"><div><span>Selected queue</span><strong>${queueLocked ? sessionQueue.length : pendingQueue.length}</strong></div><div><span>Completed</span><strong>${completedBooks}</strong></div><div><span>Failed</span><strong>${failedBooks}</strong></div><div><span>Workers</span><strong>${valueFor(session, "workerLimit", 0) || "—"}</strong></div><div><span>Elapsed</span><strong>${elapsedTime(valueFor(session, "startedAt", null))}</strong></div><div><span>Progress</span><strong>${completed} / ${total || "?"}</strong></div></div><ol class="process-stages">${stages.map((item, index) => `<li class="${index < currentStageIndex ? "complete" : index === currentStageIndex && queueLocked ? "active" : ""}"><span>${index + 1}</span>${item}</li>`).join("")}</ol></section><section class="panel process-current-stage-panel"><div class="process-panel-heading"><div><h2 class="panel-title">Current stage</h2><p>${queueLocked ? "Live progress for the active Book" : terminal ? "Final state of the last session" : "Start processing when the selected queue is ready"}</p></div></div><div class="process-book"><strong>${escapeHtml(currentBook)}</strong><span>${escapeHtml(currentStep)}</span></div><div class="progress-track"><span style="width:${percent}%"></span></div><p class="progress-copy">${completed} / ${total || "?"} pages · ${valueFor(session, "workerLimit", 0) || "?"} workers</p>${outcomeMarkup}${!selectionReadiness.ready && state.selectedBookIds.size ? `<div class="process-failure" role="alert"><strong>${selectionReadiness.mixed ? "Selected queue contains multiple Brands" : "Selected Book needs review"}</strong><p>${escapeHtml(selectionReadiness.reason)}</p></div>` : ""}${failureDetails.length ? `<div class="process-failure" role="alert"><strong>Run needs review</strong>${failureDetails.map((entry) => `<p>${escapeHtml(valueFor(valueFor(entry, "bookId", {}), "value", ""))}: ${escapeHtml(valueFor(entry, "detail", ""))}</p>`).join("")}</div>` : ""}<div class="page-actions mt-4">${queueLocked ? "" : `<button class="button-primary" data-action="start-process" ${selectionReadiness.ready ? "" : "disabled"}>${terminal ? "Start New Interior Processing" : "Start Interior Processing"}</button>`}</div></section></section>`;
    const pageDescription = cancelling
      ? `Stopping ${sessionName} session…`
      : active
        ? productionSession ? "Building and publishing the Final Interior PDF." : "Prepare Interior pages for preview. This does not build or replace a PDF."
        : terminal
          ? `Last ${sessionName} session`
          : "Prepare Interior pages for preview. This does not build or replace a PDF.";
    content.innerHTML = `<section class="process-page" aria-busy="${queueLocked}"><div class="page-header"><div><h1>${escapeHtml(sessionName)}</h1><p>${escapeHtml(pageDescription)}</p></div>${active ? cancelling ? '<button class="button-danger" disabled>Stopping processing…</button>' : '<button class="button-danger" data-action="cancel-process">Cancel session</button>' : ""}</div><nav class="process-tabs" role="tablist" aria-label="Interior Processing workspace"><button class="${state.processTab === "overview" ? "active" : ""}" data-action="process-tab" data-process-tab="overview" role="tab" aria-selected="${state.processTab === "overview"}">Overview</button><button class="${state.processTab === "queue" ? "active" : ""}" data-action="process-tab" data-process-tab="queue" role="tab" aria-selected="${state.processTab === "queue"}">Selected queue <span>${queue.length}</span></button></nav><div class="process-tab-body">${state.processTab === "queue" ? queueTab : overviewTab}</div></section>`;
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
    const outputRow = (summary, output) => {
      const pageCount = valueFor(output, "pageCount", "—");
      const dimensions = valueFor(output, "widthInches", null) ? `${inches(valueFor(output, "widthInches", 0))} × ${inches(valueFor(output, "heightInches", 0))} in` : "—";
      const fileName = valueFor(output, "fileName", "PDF output");
      const kind = valueFor(output, "artifactKind", "Unknown");
      const verification = valueFor(output, "verificationStatus", "Available");
      const previewable = ["Verified", "Available"].includes(verification);
      const id = valueFor(valueFor(summary, "bookId", {}), "value", "");
      const artifactReference = valueFor(output, "artifactReference", "");
      const actionKey = `preview:${id}:${artifactReference}`;
      const pending = state.pdfLibraryPendingActions.has(actionKey);
      const statusMarkup = previewable ? "" : `<span class="pdf-library-file-status">${escapeHtml(displayStatus(verification))}</span>`;
      const pageLabel = pageCount === 1 ? "page" : "pages";
      return `<li class="pdf-library-file"><button type="button" class="pdf-library-file-button${previewable ? "" : " is-unavailable"}" data-action="preview-output" data-book-id="${escapeHtml(id)}" data-artifact-reference="${escapeHtml(artifactReference)}" data-output-action-key="${escapeHtml(actionKey)}" data-output-idle-label="Preview ›" data-output-busy-label="Opening…" title="${escapeHtml(fileName)}" aria-busy="${pending}" ${previewable && !pending ? "" : "disabled"}><span class="pb-visually-hidden">Preview </span><span class="pdf-library-file-copy"><span class="pdf-library-file-title"><strong>${escapeHtml(kind)}</strong>${statusMarkup}</span><span class="pdf-library-file-meta">${escapeHtml(String(pageCount))} ${pageLabel} · ${escapeHtml(dimensions)} · ${fileSize(valueFor(output, "fileSizeBytes", 0))}</span></span><span class="pdf-library-file-action" data-output-action-label>${pending ? "Opening…" : "Preview ›"}</span></button></li>`;
    };
    const bookCard = ({ book, summary }) => {
      const name = pdfLibraryBookName(book, summary);
      const thumbnail = bookThumbnailMarkup(book, summary, "Cover unavailable");
      const outputs = pdfLibraryOutputs(summary);
      const id = valueFor(valueFor(summary, "bookId", {}), "value", "");
      const actionKey = `folder:${id}`;
      const pending = state.pdfLibraryPendingActions.has(actionKey);
      const folderAvailable = outputs.some((output) => valueFor(output, "verificationStatus", "Missing") !== "Missing");
      return `<article class="pdf-library-book pdf-library-book-${state.pdfLibraryView}" data-pdf-book-id="${escapeHtml(name)}"><span class="pdf-library-book-preview">${thumbnail}</span><header class="pdf-library-book-header"><h2 title="${escapeHtml(name)}">${escapeHtml(name)}</h2></header><ul class="pdf-library-files">${outputs.map((output) => outputRow(summary, output)).join("")}</ul><footer class="pdf-library-book-footer"><button type="button" class="button-secondary pdf-library-open-folder" data-action="open-output-folder" data-book-id="${escapeHtml(id)}" data-output-action-key="${escapeHtml(actionKey)}" data-output-idle-label="Open Folder" data-output-busy-label="Opening…" aria-busy="${pending}" ${folderAvailable && !pending ? "" : "disabled"}><span data-output-action-label>${pending ? "Opening…" : "Open Folder"}</span></button></footer></article>`;
    };
    const empty = eligibleTotal === 0
      ? `<section class="pdf-library-empty"><strong>No completed PDFs yet.</strong><p>Build a Cover PDF or Final Interior PDF to make it appear here.</p></section>`
      : `<section class="pdf-library-empty"><strong>No PDF Books match your search.</strong><p>Try a different Book name.</p></section>`;
    const pagination = library.length ? `<footer class="book-pagination pdf-library-pagination" data-pdf-library-total-pages="${totalPages}"><span>${start}–${end} of ${library.length}</span><div><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="first" ${state.pdfLibraryPage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="previous" ${state.pdfLibraryPage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.pdfLibraryPage} of ${totalPages}</span><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="next" ${state.pdfLibraryPage === totalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="last" ${state.pdfLibraryPage === totalPages ? "disabled" : ""}>Last</button></div></footer>` : "";
    const results = library.length ? `<section class="${state.pdfLibraryView === "grid" ? "pdf-library-grid" : "pdf-library-list"}">${pageItems.map(bookCard).join("")}</section>` : empty;
    content.innerHTML = `<section class="pdf-library-page"><div class="page-header"><div><h1>PDF Library</h1><p>Select Cover or Interior to open it in your default PDF app.</p></div></div><div class="pdf-library-toolbar"><label class="field"><span>Search Books</span><input class="control" type="search" value="${escapeHtml(state.pdfLibrarySearch)}" placeholder="Search Books..." data-action="pdf-library-search"></label><label class="field pdf-library-sort"><span>Sort</span><select class="control" data-action="pdf-library-sort"><option value="newest" ${state.pdfLibrarySort === "newest" ? "selected" : ""}>Newest</option><option value="name" ${state.pdfLibrarySort === "name" ? "selected" : ""}>Name</option><option value="size" ${state.pdfLibrarySort === "size" ? "selected" : ""}>Size</option></select></label><div class="asset-view-toggle" aria-label="PDF Library view"><button class="${state.pdfLibraryView === "grid" ? "active" : ""}" data-action="pdf-library-view" data-pdf-library-view="grid" aria-pressed="${state.pdfLibraryView === "grid"}">Grid</button><button class="${state.pdfLibraryView === "list" ? "active" : ""}" data-action="pdf-library-view" data-pdf-library-view="list" aria-pressed="${state.pdfLibraryView === "list"}">List</button></div></div><p class="pdf-library-feedback ${state.pdfLibraryFeedbackError ? "is-error" : ""}" data-pdf-library-feedback role="${state.pdfLibraryFeedbackError ? "alert" : "status"}" ${state.pdfLibraryFeedback ? "" : "hidden"}>${escapeHtml(state.pdfLibraryFeedback)}</p><section class="pdf-library-results"><div class="pdf-library-grid-scroll">${results}</div>${pagination}</section></section>`;
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
  const openBookDrawer = (id) => {
    state.selectedBookId = id;
    state.selectedBookTab = "overview";
    state.selectedAssetReference = "";
    clearArtworkBulkSelection();
    state.assetStatus = "Active";
    state.assetFrameMode = "";
    state.introTemplatePage = 1;
    state.bookDrawerScrollTop = 0;
    state.artworkGridScrollTop = 0;
    state.bookDrawerOpen = true;
    document.querySelector(".book-drawer-layer")?.remove();
    const book = selectedBook();
    if (!book) return;
    content.insertAdjacentHTML("beforeend", renderBookDrawer(book, summaryFor(book)));
    document.getElementById("book-drawer-title")?.focus();
  };
  const closeBookDrawer = () => {
    if (state.bookInteriorSavePending || state.catalogMutationPending) return;
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if ((hasInteriorDraft(state.selectedBookId) || (book && summary && hasMetadataDraft(book, summary))) && !window.confirm("Discard unsaved Book changes?")) return;
    clearInteriorDraft(state.selectedBookId);
    state.bookMetadataDrafts.delete(state.selectedBookId);
    state.subcoverTouchedBooks.delete(state.selectedBookId);
    state.bookDrawerScrollTop = 0;
    state.bookDrawerOpen = false;
    document.querySelector(".book-drawer-layer")?.remove();
    if (state.bookListRefreshPending) {
      state.bookListRefreshPending = false;
      state.restoreBookFocus = true;
      render("books", false);
      return;
    }
    document.querySelector(`[data-action="open-book-detail"][data-book-id="${CSS.escape(state.selectedBookId)}"]`)?.focus();
  };
  const beginCatalogMutation = (command, target, payload) => {
    if (state.catalogMutationPending || processIsActive()) return;
    state.catalogMutationPending = true;
    state.catalogMutationAwaitingSnapshot = false;
    state.catalogMutationCommand = command;
    state.catalogMutationTarget = target;
    state.catalogFeedback = "Saving…";
    state.catalogFeedbackError = false;
    send(command, payload);
    if (currentRoute() === "books" && state.bookDrawerOpen) updateBookCatalogMutationUi();
    if (currentRoute() === "brands") render("brands", false);
  };
  const catalogErrorMessage = (code) => ({ invalid_book_metadata: "Book Information is invalid. Single-line fields cannot contain line breaks, and Subcover must contain fewer than 100 characters.", invalid_brand_author: "Brand Author must be a single line.", book_author_required: "Save a Book Author before assigning a Brand.", brand_author_required: "The selected Brand does not have an Author.", book_brand_author_mismatch: "Book Author must match Brand Author before assignment.", brand_metadata_invalid: "Brand metadata could not be read. Fix the metadata file and retry.", processing_active: "Interior Processing is running. Try again when it finishes.", snapshot_unavailable: "The library snapshot is unavailable. Refresh and retry.", book_not_found: "This Book is no longer available. Refresh the library.", brand_not_found: "This Brand is no longer available. Refresh the library." })[code] ?? "The change could not be saved. Refresh and retry.";
  document.addEventListener("keydown", (event) => {
    const activeTab = event.target.closest?.('[role="tab"][data-action="book-tab"]');
    if (activeTab && ["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
      const tabs = [...activeTab.closest('[role="tablist"]')?.querySelectorAll('[role="tab"][data-action="book-tab"]') ?? []];
      if (tabs.length) {
        event.preventDefault();
        const current = tabs.indexOf(activeTab);
        const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (current + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
        tabs[next].click();
      }
      return;
    }
    if (event.key !== "Escape" || !state.bookDrawerOpen) return;
    event.preventDefault();
    closeBookDrawer();
  });
  content.addEventListener("click", (event) => {
    const updateAction = event.target.closest("[data-update-action]")?.dataset.updateAction;
    if (updateAction) { beginUpdateAction(updateAction); return; }
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
    if (action === "select-brand") { state.inspectedBrand = target.dataset.brandName; state.brandValidationResult = null; render("brands"); }
    if (action === "validate-brand") { const requestId = send("brand.validate", { brandName: state.inspectedBrand }); state.brandValidationRequestBrands.set(requestId, state.inspectedBrand); }
    if (action === "save-brand-author") {
      const brandName = target.dataset.brandName;
      beginCatalogMutation("brand.author.save", brandName, { brandName, author: state.brandAuthorDrafts.get(brandName) ?? "" });
    }
    if (action === "save-book-metadata") {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const summary = book ? summaryFor(book) : null;
      const draft = book && summary ? metadataDraftFor(book, summary, true) : null;
      const error = draft ? subcoverError(draft.subcover) : "Book not found.";
      if (error) { state.catalogFeedback = error; state.catalogFeedbackError = true; refreshBookDrawerBody(); return; }
      beginCatalogMutation("book.metadata.save", target.dataset.bookId, { bookId: target.dataset.bookId, ...draft });
    }
    if (action === "assign-book-brand") {
      const bookIdValue = target.dataset.bookId;
      const summary = summaryFor(books().find((item) => bookId(item) === bookIdValue));
      const select = document.querySelector('[data-action="book-brand-select"]');
      const brandName = select?.value ?? "";
      if (!brandName) { state.catalogFeedback = "Select a matching Brand first."; state.catalogFeedbackError = true; refreshBookDrawerBody(); return; }
      const current = assignedBrandName(summary);
      if (current && current !== brandName && !window.confirm(`Reassign this Book from '${current}' to '${brandName}'? Existing files and outputs will not be moved or changed.`)) return;
      beginCatalogMutation("book.brand.assign", bookIdValue, { bookId: bookIdValue, brandName });
    }
    if (action === "unassign-book-brand") {
      const bookIdValue = target.dataset.bookId;
      if (!window.confirm("Unassign this Book from its Brand? Existing files and outputs will be kept.")) return;
      beginCatalogMutation("book.brand.unassign", bookIdValue, { bookId: bookIdValue });
    }
    if (action === "copy-brand-templates" && !state.brandTemplateCopyPending) {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const readiness = book ? brandTemplateCopyReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.brandTemplateCopyPending = true;
      refreshBookDrawerBody();
      send("book.brand.templates.copy", { bookId: target.dataset.bookId });
    }
    if (action === "upload-production-asset" && !state.productionImportPending && !productionActionActive()) {
      state.productionImportPending = target.dataset.productionAsset;
      state.productionFocusSelector = `[data-action="upload-production-asset"][data-production-asset="${target.dataset.productionAsset}"]`;
      state.productionFeedback = "Choose a PNG file in the system dialog.";
      state.productionFeedbackError = false;
      updateProductionInteractionUi();
      send("book.production.asset.import", { bookId: target.dataset.bookId, assetKind: target.dataset.productionAsset });
    }
    if (action === "start-production-action" && !productionActionActive()) {
      state.productionActionName = target.dataset.productionAction;
      state.productionFocusSelector = `[data-action="start-production-action"][data-production-action="${target.dataset.productionAction}"]`;
      state.productionFeedback = "Queuing Production action…";
      state.productionFeedbackError = false;
      updateProductionInteractionUi();
      send("book.production.action.start", { bookId: target.dataset.bookId, action: target.dataset.productionAction });
    }
    if (action === "build-final-interior" && !state.processStartPending) {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const readiness = book ? productionFinalReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { state.productionFeedback = readiness.reason; state.productionFeedbackError = true; updateProductionInteractionUi(); return; }
      state.processStartPending = true;
      state.productionFinalBuildActive = true;
      state.productionFocusSelector = '[data-action="build-final-interior"]';
      state.productionFeedback = "Starting Final Interior build…";
      state.productionFeedbackError = false;
      updateProductionInteractionUi();
      send("process.start", { bookIds: [target.dataset.bookId], mode: "production-interior" });
    }
    if (action === "select-book" || action === "open-book-detail") openBookDrawer(target.dataset.bookId);
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
        refreshIntroTemplateWorkspace();
      }
    }
    if (action === "intro-template-page") {
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      const selection = book && summary ? effectiveIntro(book, summary) : null;
      const itemCount = selection?.hasIntro ? assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior").length : (valueFor(assignedBrandFor(summary), "introTemplateAssets", []) ?? []).filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", ""))).length;
      const last = Math.max(1, Math.ceil(itemCount / 6));
      state.introTemplatePage = Math.min(last, Math.max(1, state.introTemplatePage + (target.dataset.introTemplatePage === "next" ? 1 : -1)));
      refreshIntroTemplateWorkspace(target.dataset.introTemplatePage);
    }
    if (action === "toggle-book-selection") {
      const id = target.dataset.bookId;
      const book = books().find((item) => bookId(item) === id);
      if (!book || !workspaceStateAvailable(summaryFor(book))) { status.textContent = "This Book cannot be selected because its workspace state is unavailable."; return; }
      if (state.selectedBookIds.has(id)) state.selectedBookIds.delete(id); else state.selectedBookIds.add(id);
      status.textContent = state.selectedBookIds.size ? `${state.selectedBookIds.size} Book${state.selectedBookIds.size === 1 ? "" : "s"} selected` : "Selection cleared";
      refreshBookSelectionUi();
    }
    if (action === "toggle-book-page-selection") {
      const pageSize = 12;
      const pageItems = filteredBooks().slice((state.bookPage - 1) * pageSize, state.bookPage * pageSize);
      pageItems.filter((book) => workspaceStateAvailable(summaryFor(book))).forEach((book) => { if (target.checked) state.selectedBookIds.add(bookId(book)); else state.selectedBookIds.delete(bookId(book)); });
      status.textContent = state.selectedBookIds.size ? `${state.selectedBookIds.size} Book${state.selectedBookIds.size === 1 ? "" : "s"} selected` : "Selection cleared";
      refreshBookSelectionUi();
    }
    if (action === "select-all-filtered-books") {
      filteredBooks().filter((book) => workspaceStateAvailable(summaryFor(book))).forEach((book) => state.selectedBookIds.add(bookId(book)));
      status.textContent = `${state.selectedBookIds.size} Book${state.selectedBookIds.size === 1 ? "" : "s"} selected`;
      refreshBookSelectionUi();
    }
    if (action === "clear-book-selection") {
      state.selectedBookIds.clear();
      status.textContent = "Selection cleared";
      refreshBookSelectionUi();
    }
    if (action === "queue-book") { const id = target.dataset.bookId; if (target.checked) state.selectedBookIds.add(id); else state.selectedBookIds.delete(id); }
    if (action === "queue-selected-book") {
      const book = books().find((item) => bookId(item) === state.selectedBookId);
      const readiness = book ? processingReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.selectedBookIds.add(state.selectedBookId);
      state.processTab = "queue";
      state.processQueuePage = 1;
      render("process");
    }
    if (action === "toggle-artwork-selection") {
      const reference = target.dataset.sourceReference;
      if (state.selectedArtworkReferences.has(reference)) state.selectedArtworkReferences.delete(reference); else state.selectedArtworkReferences.add(reference);
      refreshInteriorArtworkWorkspace();
    }
    if (action === "toggle-all-artwork") {
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      if (book && summary) {
        const intro = effectiveIntro(book, summary);
        const folders = valueFor(summary, "sourceFolders", []).map((folder) => valueFor(folder, "name", "")).filter(Boolean);
        const folderFor = (asset) => folders.find((name) => valueFor(asset, "relativePath", "").replaceAll("\\", "/").toLowerCase().startsWith(`${name.toLowerCase()}/`)) ?? valueFor(asset, "folder", "Other");
        const shown = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior" && `${valueFor(asset, "fileName", "")} ${valueFor(asset, "relativePath", "")}`.toLowerCase().includes(state.assetFilter.toLowerCase()) && (!state.assetStatus || (state.assetStatus === "Active" ? effectiveInteriorAsset(book, asset).isActive : !effectiveInteriorAsset(book, asset).isActive)) && (!state.assetFrameMode || effectiveInteriorAsset(book, asset).frameMode === state.assetFrameMode) && !intro.sourceReferences.some((reference) => reference.toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase()));
        shown.forEach((asset) => { const reference = String(valueFor(asset, "sourceReference", "")); if (target.checked) state.selectedArtworkReferences.add(reference); else state.selectedArtworkReferences.delete(reference); });
        refreshInteriorArtworkWorkspace();
      }
    }
    if (action === "apply-artwork-bulk") {
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      if (book && summary && state.selectedArtworkReferences.size) {
        const intro = effectiveIntro(book, summary);
        const selectedAssets = assetsFor(summary).filter((asset) => state.selectedArtworkReferences.has(String(valueFor(asset, "sourceReference", ""))) && !intro.sourceReferences.some((reference) => reference.toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase()));
        selectedAssets.forEach((asset) => {
          if (state.assetBulkActive !== "unchanged") stageInteriorAssetChange(book, asset, "active", state.assetBulkActive === "active");
          if (state.assetBulkFrameMode !== "unchanged") stageInteriorAssetChange(book, asset, "frameMode", state.assetBulkFrameMode);
        });
        status.textContent = `Applied Interior changes to ${selectedAssets.length} artwork`;
        refreshInteriorArtworkWorkspace();
      }
    }
    if (action === "book-tab") { state.selectedBookTab = ["production", "settings", "artwork", "pages"].includes(target.dataset.bookTab) ? target.dataset.bookTab : "overview"; refreshBookDrawerBody(state.selectedBookTab); }
    if (action === "select-asset") { state.selectedAssetReference = target.dataset.sourceReference; render("books", false); }
    if (action === "asset-view") { state.assetView = target.dataset.assetView; render("books", false); }
    if (action === "asset-status") { const status = ["Active", "Inactive"].includes(target.dataset.assetStatus) ? target.dataset.assetStatus : ""; state.assetStatus = state.assetStatus === status ? "" : status; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (action === "asset-frame-mode") { const mode = ["", "enabled", "disabled"].includes(target.dataset.assetFrameMode) ? target.dataset.assetFrameMode : ""; state.assetFrameMode = mode; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (action === "clear-artwork-filters") { state.assetFilter = ""; state.assetStatus = "Active"; state.assetFrameMode = ""; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (action === "book-view") { state.bookView = target.dataset.bookView; render("books", false); }
    if (action === "clear-cache" && !cacheCleanupBlocked()) {
      if (window.confirm("Clear processed image cache for completed Books?")) {
        state.cacheCleanupResultRequested = false;
        send("cache.clear");
      }
    }
    if (action === "book-page") { const last = Number(target.closest("[data-book-total-pages]")?.dataset.bookTotalPages ?? 1); state.bookPage = target.dataset.bookPage === "first" ? 1 : target.dataset.bookPage === "last" ? last : Math.min(last, Math.max(1, state.bookPage + (target.dataset.bookPage === "next" ? 1 : -1))); render("books", false); }
    if (action === "pdf-library-page") { const totalPages = Math.max(1, Math.ceil(pdfLibraryBooks().length / pdfLibraryPageSize)); state.pdfLibraryPage = target.dataset.pdfLibraryPage === "first" ? 1 : target.dataset.pdfLibraryPage === "last" ? totalPages : Math.min(totalPages, Math.max(1, state.pdfLibraryPage + (target.dataset.pdfLibraryPage === "next" ? 1 : -1))); render("outputs", false); }
    if (action === "pdf-library-view") { state.pdfLibraryView = target.dataset.pdfLibraryView === "list" ? "list" : "grid"; render("outputs", false); }
    if (action === "process-tab") { state.processTab = target.dataset.processTab === "queue" ? "queue" : "overview"; render("process", false); }
    if (action === "process-queue-page") { const last = Number(target.closest("[data-process-queue-total-pages]")?.dataset.processQueueTotalPages ?? 1); state.processQueuePage = target.dataset.processQueuePage === "first" ? 1 : target.dataset.processQueuePage === "last" ? last : Math.min(last, Math.max(1, state.processQueuePage + (target.dataset.processQueuePage === "next" ? 1 : -1))); render("process", false); }
    if (action === "remove-process-queue-book" && !processIsActive()) {
      const id = target.dataset.bookId;
      const book = books().find((item) => bookId(item) === id);
      const name = valueFor(book, "name", id);
      if (!window.confirm(`Remove ${name} from the selected queue?`)) return;
      state.selectedBookIds.delete(id);
      status.textContent = `${name} removed from selected queue`;
      render("process", false);
    }
    if (action === "validate-book") send("book.validate", { bookId: target.dataset.bookId });
    if (action === "go-process") render("process");
    if (action === "go-books") render("books", false);
    if (action === "start-process" && !state.processStartPending) {
      const readiness = selectedProcessingReadiness();
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.processStartPending = true;
      send("process.start", { bookIds: [...state.selectedBookIds], mode: "interior-only" });
    }
    if (action === "cancel-process") send("process.cancel");
    if (action === "preview-output") beginPdfLibraryAction("book.output.preview", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference }, `preview:${target.dataset.bookId}:${target.dataset.artifactReference}`, target);
    if (action === "open-output-folder") beginPdfLibraryAction("book.output.open-folder", { bookId: target.dataset.bookId }, `folder:${target.dataset.bookId}`, target);
    if (action === "open-output") send("book.output.open", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
    if (action === "reveal-output") send("book.output.reveal", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
    if (action === "copy-output-path") send("book.output.copy-path", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
  });
  content.addEventListener("input", (event) => {
    if (event.target.dataset.action === "filter-books") { state.bookFilter = event.target.value; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "filter-brands") { state.brandFilter = event.target.value; refreshBrandList(); }
    if (event.target.dataset.action === "filter-assets") { state.assetFilter = event.target.value; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (event.target.dataset.action === "pdf-library-search") { state.pdfLibrarySearch = event.target.value; state.pdfLibraryPage = 1; state.pdfLibrarySearchFocused = true; state.pdfLibrarySearchCaret = event.target.selectionStart ?? event.target.value.length; render("outputs", false); }
    if (event.target.dataset.action === "book-metadata-input") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      const summary = book ? summaryFor(book) : null;
      if (book && summary) {
        const draft = metadataDraftFor(book, summary, true);
        draft[event.target.dataset.metadataField] = event.target.value;
        const error = subcoverError(draft.subcover);
        const save = document.querySelector('[data-action="save-book-metadata"]');
        if (save) save.disabled = !hasMetadataDraft(book, summary) || Boolean(error) || state.catalogMutationPending || processIsActive();
        const subcover = document.querySelector('[data-metadata-field="subcover"]');
        const errorElement = document.getElementById("book-subcover-error");
        const showError = Boolean(error) && state.subcoverTouchedBooks.has(bookId(book));
        subcover?.classList.toggle("control-invalid", showError);
        subcover?.setAttribute("aria-invalid", String(showError));
        if (errorElement) { errorElement.hidden = !showError; errorElement.textContent = error; }
        const assignmentWarning = metadataAssignmentWarning(draft, summary);
        const assignmentWarningElement = document.getElementById("book-author-assignment-warning");
        if (assignmentWarningElement) { assignmentWarningElement.hidden = !assignmentWarning; assignmentWarningElement.textContent = assignmentWarning; }
        const unsaved = document.querySelector("[data-book-interior-unsaved]");
        if (unsaved) unsaved.hidden = !(hasInteriorDraft(bookId(book)) || hasMetadataDraft(book, summary));
      }
    }
    if (event.target.dataset.action === "brand-author-input") {
      state.brandAuthorDrafts.set(event.target.dataset.brandName, event.target.value);
      const selected = brands().find((brand) => valueFor(brand, "name", "") === event.target.dataset.brandName);
      const save = document.querySelector('[data-action="save-brand-author"]');
      if (save) save.disabled = !selected || !brandAuthorIsDirty(selected, event.target.value) || state.catalogMutationPending || processIsActive();
    }
  });
  content.addEventListener("change", (event) => {
    if (event.target.dataset.action === "book-status") { state.bookStatus = bookStatuses.includes(event.target.value) ? event.target.value : "All"; state.bookPage = 1; render("books", false); }
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
        state.introTemplatePage = 1;
        status.textContent = "Unsaved Intro and Interior changes";
        refreshIntroTemplateWorkspace();
      }
    }
    if (event.target.dataset.action === "set-artwork-bulk-active") { state.assetBulkActive = ["active", "inactive"].includes(event.target.value) ? event.target.value : "unchanged"; refreshInteriorArtworkWorkspace(); }
    if (event.target.dataset.action === "set-artwork-bulk-frame-mode") { state.assetBulkFrameMode = ["enabled", "disabled"].includes(event.target.value) ? event.target.value : "unchanged"; refreshInteriorArtworkWorkspace(); }
    if (event.target.dataset.action === "book-sort") { state.bookSort = event.target.value; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "book-brand-filter") { state.bookBrandFilter = event.target.value; state.bookPage = 1; state.selectedBookIds.clear(); render("books", false); }
    if (event.target.dataset.action === "book-brand-select") { const assign = document.querySelector('[data-action="assign-book-brand"]'); const book = selectedBook(); const summary = book ? summaryFor(book) : null; if (assign) assign.disabled = !event.target.value || event.target.value === assignedBrandName(summary) || catalogMutationBusy() || processIsActive(); }
    if (event.target.dataset.action === "book-metadata-input" && event.target.dataset.metadataField === "subcover") { state.subcoverTouchedBooks.add(event.target.dataset.bookId); refreshBookDrawerBody(); }
    if (event.target.dataset.action === "brand-author-input") render("brands", false);
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
    if (image.dataset.introTemplateId && state.bookDrawerOpen && state.selectedBookTab === "settings") render("books", false);
  }, true);
  content.addEventListener("load", (event) => {
    const image = event.target;
    if (!image?.matches?.("img[data-local-image]") || !image.dataset.introTemplateId) return;
    const valid = isSupportedIntroTemplateSize(image.naturalWidth, image.naturalHeight);
    const previous = state.introTemplateDimensions.get(image.dataset.introTemplateId);
    if (previous?.valid === valid && previous.width === image.naturalWidth && previous.height === image.naturalHeight) return;
    state.introTemplateDimensions.set(image.dataset.introTemplateId, { valid, width: image.naturalWidth, height: image.naturalHeight });
    if (state.bookDrawerOpen && state.selectedBookTab === "settings") render("books", false);
  }, true);
  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const responseId = valueFor(response, "id", "");
    const requestCommand = state.pendingCommands.get(responseId) ?? "";
    const validationRequestBrand = state.brandValidationRequestBrands.get(responseId) ?? "";
    const pdfLibraryAction = finishPdfLibraryAction(responseId);
    state.pendingCommands.delete(responseId);
    state.brandValidationRequestBrands.delete(responseId);
    const ok = valueFor(response, "ok", false);
    const command = valueFor(response, "command", "");
    if (ok && command === "updates.state") {
      applyUpdateSnapshot(valueFor(response, "payload", {}));
    } else if (ok && command === "app.pong") {
      status.textContent = "Connected";
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "LibraryRefresh") {
      if (requestCommand === "book.interior.settings.save") {
        state.bookInteriorSaveTaskId = valueFor(valueFor(response, "payload", {}), "taskId", "");
        state.bookInteriorSavePending = false;
        clearInteriorDraft(state.selectedBookId);
        clearArtworkBulkSelection();
        status.textContent = "Interior changes saved";
        updateInteriorSaveUi();
      }
      if (["book.metadata.save", "book.brand.assign", "book.brand.unassign", "brand.author.save"].includes(requestCommand)) {
        state.catalogMutationPending = false;
        state.catalogMutationAwaitingSnapshot = true;
        state.catalogFeedback = "Saved. Refreshing library…";
        state.catalogFeedbackError = false;
        if (currentRoute() === "books" && state.bookDrawerOpen) updateBookCatalogMutationUi();
        if (currentRoute() === "brands") render("brands", false);
      }
      observeLibraryRefresh(valueFor(response, "payload", {}));
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "CacheCleanup") {
      observeCacheCleanup(valueFor(response, "payload", {}));
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "ProductionAction") {
      observeProductionAction(valueFor(response, "payload", {}));
    } else if (ok && command === "app.snapshot") {
      const preserveBookDrawer = state.bookInteriorSaveAwaitingSnapshot && state.bookDrawerOpen && currentRoute() === "books";
      const preserveProductionDrawer = state.productionRefreshAwaitingSnapshot && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books";
      const preserveCatalogDrawer = state.catalogMutationAwaitingSnapshot && state.bookDrawerOpen && currentRoute() === "books" && state.catalogMutationCommand.startsWith("book.");
      state.bookInteriorSaveAwaitingSnapshot = false;
      state.bookInteriorSaveTaskId = "";
      state.productionRefreshAwaitingSnapshot = false;
      const catalogWasAwaiting = state.catalogMutationAwaitingSnapshot;
      const catalogTarget = state.catalogMutationTarget;
      const catalogCommand = state.catalogMutationCommand;
      window.appSnapshot = valueFor(response, "payload", {});
      if (catalogWasAwaiting) {
        if (catalogCommand === "book.metadata.save") { state.bookMetadataDrafts.delete(catalogTarget); state.subcoverTouchedBooks.delete(catalogTarget); }
        if (catalogCommand === "brand.author.save") state.brandAuthorDrafts.delete(catalogTarget);
        state.catalogMutationAwaitingSnapshot = false;
        state.catalogFeedback = "Saved";
        state.catalogFeedbackError = false;
      }
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      state.libraryRefreshTaskId = "";
      state.libraryRefreshResultRequested = false;
      const allBrands = valueFor(discovery(), "brands", []);
      if (!allBrands.some((brand) => valueFor(brand, "name", "") === state.inspectedBrand)) state.inspectedBrand = valueFor(allBrands[0], "name", "");
      if (preserveBookDrawer) {
        if (state.selectedBookTab === "artwork") refreshInteriorArtworkWorkspace();
        else updateInteriorSaveUi();
        status.textContent = "Interior changes saved";
      } else if (preserveProductionDrawer) {
        state.productionFeedback = state.productionFeedback.replace(" Refreshing status…", "");
        refreshProductionWorkspace(state.productionFocusSelector);
        state.productionFocusSelector = "";
        updateGlobalRefreshControl();
        status.textContent = catalogWasAwaiting ? "Catalog changes saved" : "Connected";
      } else if (preserveCatalogDrawer) {
        refreshBrandDependentBookUi(catalogCommand);
        state.bookListRefreshPending = true;
        updateGlobalRefreshControl();
        status.textContent = "Catalog changes saved";
      } else {
        render(document.querySelector(".nav-item-active")?.dataset.route ?? "books", false);
        status.textContent = "Connected";
      }
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
      const snapshot = valueFor(response, "payload", {});
      if (isStaleProcessSnapshot(snapshot)) return;
      window.processSnapshot = snapshot;
      state.processStartPending = false;
      const startedAt = valueFor(window.processSnapshot, "startedAt", "");
      const terminal = !valueFor(window.processSnapshot, "isActive", false) && !valueFor(window.processSnapshot, "isCancelling", false);
      const productionSession = processMode(window.processSnapshot) === "production-interior";
      if (terminal && startedAt && state.lastTerminalRefreshSession !== startedAt) {
        state.lastTerminalRefreshSession = startedAt;
        if (productionSession && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
          state.productionRefreshAwaitingSnapshot = true;
        }
        beginApplicationRefresh();
      }
      updateGlobalProcessStatus();
      if (document.querySelector(".nav-item-active")?.dataset.route === "process") {
        render("process", false);
        if (terminal) window.requestAnimationFrame?.(() => document.querySelector("[data-process-failure-summary]")?.focus?.());
      }
      if (state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
        state.productionFeedback = productionSession && valueFor(window.processSnapshot, "isActive", false)
          ? valueFor(window.processSnapshot, "currentStep", "Building Final Interior…")
          : state.productionFeedback;
        updateProductionInteractionUi();
      }
      if (terminal) state.productionFinalBuildActive = false;
      status.textContent = "Connected";
    } else if (ok && command === "brand.validation.result") {
      state.brandValidationResult = { ...valueFor(response, "payload", {}), brandName: validationRequestBrand };
      if (currentRoute() === "brands") render("brands", false);
      content.querySelector?.("[data-brand-validation-summary]")?.focus?.();
      status.textContent = valueFor(state.brandValidationResult, "isSuccess", false) ? "Brand validation completed" : "Brand validation needs attention";
      beginApplicationRefresh();
    } else if (ok && command === "book.brand.templates.copied") {
      state.brandTemplateCopyPending = false;
      if (state.bookDrawerOpen && currentRoute() === "books") refreshBookDrawerBody();
      status.textContent = "Brand templates copied";
    } else if (ok && command === "book.production.asset.import.cancelled") {
      const assetKind = state.productionImportPending;
      state.productionImportPending = "";
      state.productionFeedback = "";
      state.productionFeedbackError = false;
      if (state.bookDrawerOpen && state.selectedBookTab === "production") {
        updateProductionInteractionUi();
        document.querySelector(`[data-action="upload-production-asset"][data-production-asset="${assetKind}"]`)?.focus();
      }
      state.productionFocusSelector = "";
    } else if (ok && command === "book.production.asset.imported") {
      state.productionImportPending = "";
      state.productionFeedback = "Production PNG imported. Refreshing status…";
      state.productionFeedbackError = false;
      if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      state.productionRefreshAwaitingSnapshot = true;
      beginApplicationRefresh();
    } else if (ok && command === "book.output.action.completed") {
      const fallback = valueFor(valueFor(response, "payload", {}), "fallbackToOriginal", false);
      const message = fallback
        ? "Lightweight preview unavailable; opened the original PDF."
        : requestCommand === "book.output.open-folder"
          ? "Opened the Book output folder."
          : "Opened the PDF preview.";
      setPdfLibraryFeedback(message);
      status.textContent = message;
    } else if (ok && command === "diagnostics.snapshot") {
      window.uiDiagnostics = valueFor(response, "payload", []);
      if (currentRoute() === "diagnostics") render("diagnostics", false);
      status.textContent = "Diagnostics refreshed";
    } else if (ok && command === "background.tasks") {
      state.backgroundTasks = valueFor(response, "payload", []);
      if (currentRoute() === "diagnostics") render("diagnostics", false);
    } else {
      if (requestCommand.startsWith("updates.")) {
        state.updateCommandPending = "";
        updateUpdateControls();
        stopUpdatePolling();
      }
      const error = valueFor(response, "error", "unexpected response");
      if (pdfLibraryAction) {
        const message = pdfLibraryActionError(String(error).split(":", 1)[0]);
        setPdfLibraryFeedback(message, true);
        status.textContent = message;
        return;
      }
      if (requestCommand === "book.brand.templates.copy") {
        state.brandTemplateCopyPending = false;
        if (state.bookDrawerOpen && currentRoute() === "books") refreshBookDrawerBody();
      }
      if (requestCommand === "book.interior.settings.save") {
        state.bookInteriorSavePending = false;
        updateInteriorSaveUi();
      }
      if (["book.metadata.save", "book.brand.assign", "book.brand.unassign", "brand.author.save"].includes(requestCommand)) {
        state.catalogMutationPending = false;
        state.catalogMutationAwaitingSnapshot = false;
        state.catalogFeedback = catalogErrorMessage(error);
        state.catalogFeedbackError = true;
        if (currentRoute() === "books" && state.bookDrawerOpen) updateBookCatalogMutationUi();
        if (currentRoute() === "brands") render("brands", false);
      }
      if (requestCommand === "book.production.asset.import") {
        state.productionImportPending = "";
        state.productionFeedback = error === "production_cover_size_invalid"
          ? "Final Cover must be exactly 5242 × 2626 px. The previous asset was kept."
          : "The Production asset could not be imported. The previous asset was kept.";
        state.productionFeedbackError = true;
        if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      }
      if (requestCommand === "book.production.action.start") {
        state.productionActionName = "";
        state.productionFeedback = error === "production_action_active"
          ? "Another Production action is already running."
          : error === "processing_active"
            ? "Interior Processing is running."
            : error === "cache_cleanup_active"
              ? "Clear Cache is running."
              : "Production action could not start. The previous asset or PDF was kept.";
        state.productionFeedbackError = true;
        if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      }
      if (requestCommand === "app.refresh") {
        if (state.productionRefreshAwaitingSnapshot && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
          state.productionRefreshAwaitingSnapshot = false;
          state.applicationLoadState = window.appSnapshot ? "ready" : "idle";
          state.applicationLoadError = "";
          state.productionFeedback = "Production status refresh failed. The completed file was kept; use Refresh to retry.";
          state.productionFeedbackError = true;
          updateGlobalRefreshControl();
          updateProductionInteractionUi();
          status.textContent = "Production status refresh failed";
          return;
        }
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
      if (requestCommand === "cache.clear" && ["cache_cleanup_processing_active", "cache_cleanup_refresh_active", "cache_cleanup_production_active"].includes(error)) {
        state.cacheCleanupTaskId = "";
        state.cacheCleanupResultRequested = false;
        state.cacheCleanupActive = false;
        status.textContent = error === "cache_cleanup_processing_active" ? "Interior Processing is running" : error === "cache_cleanup_production_active" ? "A Production action is running" : "Library refresh is running";
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
        const productionBuildFailedToStart = state.productionFinalBuildActive;
        state.processStartPending = false;
        state.productionFinalBuildActive = false;
        if (error === "cache_cleanup_active") {
          status.textContent = "Clear Cache is running";
          return;
        }
        if (error === "production_action_active") {
          state.productionFeedback = "Wait for the active Production action to finish.";
          state.productionFeedbackError = true;
          if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
          return;
        }
        if (productionBuildFailedToStart && state.bookDrawerOpen && state.selectedBookTab === "production") {
          state.productionFeedback = "Final Interior could not start. The previous Interior PDF was kept.";
          state.productionFeedbackError = true;
          updateProductionInteractionUi();
        }
      }
      status.textContent = `Bridge error: ${error}`;
    }
  });

  const refreshButton = document.getElementById("refresh-button");
  if (refreshButton) refreshButton.addEventListener("click", beginApplicationRefresh);
  document.getElementById("update-dialog-root")?.addEventListener("click", (event) => {
    const action = event.target.closest("[data-update-action]")?.dataset.updateAction;
    if (action) beginUpdateAction(action);
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !updateIsBusy() && !document.getElementById("update-dialog-root")?.hidden) dismissUpdateDialog();
  });
  const globalProcessStatus = document.getElementById("global-process-status");
  if (globalProcessStatus) globalProcessStatus.addEventListener("click", () => render("process"));
  updateGlobalProcessStatus();
  window.setInterval(() => { if (valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false)) send("process.get"); }, 1000);
  send("app.ping");
  beginUpdateCheck();
  state.applicationLoadState = "loading";
  render("books", false);
  send("app.refresh");
})();
