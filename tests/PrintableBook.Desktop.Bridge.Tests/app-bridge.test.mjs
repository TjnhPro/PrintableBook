import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";
import vm from "node:vm";

const appScriptPath = join(
  process.cwd(),
  "src",
  "PrintableBook.Desktop",
  "Frontend",
  "js",
  "app.js"
);

function loadBridge(activeRoute = null, visibleTiles = []) {
  const status = { textContent: "" };
  const contentListeners = {};
  const documentListeners = {};
  const content = {
    innerHTML: "",
    addEventListener: (eventName, handler) => { contentListeners[eventName] = handler; },
    insertAdjacentHTML: (_position, markup) => { content.innerHTML += markup; }
  };
  const brandSelect = { innerHTML: "", value: "", addEventListener: () => { } };
  const brandSettingsEditor = { dataset: { brandSettings: "" }, value: "{}" };
  const refreshButton = {
    disabled: false,
    textContent: "Refresh",
    attributes: {},
    addEventListener: () => { },
    setAttribute: (name, value) => { refreshButton.attributes[name] = value; }
  };
  const messages = [];
  const intervals = [];
  const routeButtons = ["configuration", "brands", "books", "process", "outputs", "diagnostics"].map((route) => {
    const listeners = {};
    return { dataset: { route }, listeners, classList: { toggle: () => { } }, addEventListener: (eventName, handler) => { listeners[eventName] = handler; } };
  });
  let messageHandler;
  const browserWindow = {
    appSnapshot: activeRoute === "process" ? { discovery: { brands: [], books: [] }, bookSummaries: [] } : undefined,
    innerWidth: 1600,
    innerHeight: 900,
    chrome: {
      webview: {
        addEventListener: (_eventName, handler) => { messageHandler = handler; },
        postMessage: (message) => { messages.push(JSON.parse(message)); }
      }
    },
    setInterval: (callback) => { intervals.push(callback); return intervals.length; },
    clearInterval: () => { },
    confirm: () => true,
    requestAnimationFrame: (callback) => { callback(); return 1; }
  };

  vm.runInNewContext(readFileSync(appScriptPath, "utf8"), {
    crypto: { randomUUID: () => "request-1" },
    document: {
      getElementById: (id) => ({ "bridge-status": status, "app-content": content, "brand-select": brandSelect, "refresh-button": refreshButton }[id]),
      createElement: (tagName) => ({ tagName, className: "", textContent: "", attributes: {}, setAttribute(name, value) { this.attributes[name] = value; } }),
      querySelectorAll: (selector) => selector === "[data-preview-book-id][data-source-reference]" ? visibleTiles : selector === "[data-route]" ? routeButtons : [],
      querySelector: (selector) => selector === "[data-brand-settings]" ? brandSettingsEditor : selector === ".nav-item-active" && activeRoute ? { dataset: { route: activeRoute } } : null,
      addEventListener: (eventName, handler) => { documentListeners[eventName] = handler; }
    },
    window: browserWindow,
    CSS: { escape: (value) => String(value).replace(/["\\]/g, "\\$&") }
  });

  return { messageHandler, status, content, brandSelect, brandSettingsEditor, refreshButton, contentListeners, documentListeners, routeButtons, intervals, messages, browserWindow };
}

const pdfLibrarySnapshot = () => ({
  discovery: {
    brands: [],
    books: [
      { id: { value: "Book Alpha" }, name: "Book Alpha" },
      { id: { value: "Book Beta" }, name: "Book Beta" },
      { id: { value: "Book Gamma" }, name: "Book Gamma" },
      { id: { value: "Book Delta" }, name: "Book Delta" }
    ]
  },
  globalSettings: {},
  bookSummaries: [
    {
      bookId: { value: "Book Alpha" }, workspaceStatus: "Completed", lastRunAt: "2026-08-25T10:00:00Z", interiorSourcePageCount: 40, activeInteriorSourcePageCount: 40,
      validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [],
      outputSummaries: [
        { artifactReference: "D:\\PrintableBook\\sources\\Book Alpha\\Output\\Book Alpha - Interior.pdf", fileName: "Book Alpha - Interior.pdf", verificationStatus: "Verified", generatedAt: "2026-08-25T10:00:00Z", pageCount: 80, widthInches: 8.5, heightInches: 8.5, fileSizeBytes: 80 * 1024 * 1024 },
        { artifactReference: "D:\\PrintableBook\\sources\\Book Alpha\\Output\\Book Alpha - Cover.pdf", fileName: "Book Alpha - Cover.pdf", verificationStatus: "Verified", generatedAt: "2026-08-25T10:00:00Z", pageCount: 1, widthInches: 17, heightInches: 11, fileSizeBytes: 12 * 1024 * 1024 }
      ]
    },
    {
      bookId: { value: "Book Beta" }, workspaceStatus: "Failed", lastRunAt: "2026-08-26T11:00:00Z", interiorSourcePageCount: 20, activeInteriorSourcePageCount: 20,
      validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [],
      outputSummaries: [{ artifactReference: "D:\\PrintableBook\\sources\\Book Beta\\Output\\Book Beta - Interior.pdf", fileName: "Book Beta - Interior.pdf", verificationStatus: "Available", generatedAt: "2026-08-24T10:00:00Z", pageCount: 20, widthInches: 8.5, heightInches: 8.5, fileSizeBytes: 25 * 1024 * 1024 }]
    },
    {
      bookId: { value: "Book Gamma" }, workspaceStatus: "Completed", lastRunAt: "2026-08-26T12:00:00Z", interiorSourcePageCount: 30, activeInteriorSourcePageCount: 30,
      validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [], outputSummaries: []
    },
    {
      bookId: { value: "Book Delta" }, workspaceStatus: "Completed", lastRunAt: "2026-08-26T13:00:00Z", interiorSourcePageCount: 24, activeInteriorSourcePageCount: 24,
      validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [],
      outputSummaries: [{ artifactReference: "D:\\PrintableBook\\sources\\Book Delta\\Output\\Book Delta - Interior.pdf", fileName: "Book Delta - Interior.pdf", verificationStatus: "Verified", generatedAt: "2026-08-26T13:00:00Z", pageCount: 48, widthInches: 8.5, heightInches: 8.5, fileSizeBytes: 42 * 1024 * 1024 }]
    }
  ]
});

test("bridge accepts the JSON response emitted by the .NET host", () => {
  const { messageHandler, status } = loadBridge();

  messageHandler({
    data: JSON.stringify({
      version: 1,
      id: "request-1",
      ok: true,
      command: "app.pong",
      error: null
    })
  });

  assert.equal(status.textContent, "Connected");
});

test("webview shell exposes every top-level desktop route", () => {
  const page = readFileSync(join(process.cwd(), "src", "PrintableBook.Desktop", "Frontend", "index.html"), "utf8");

  for (const route of ["configuration", "brands", "books", "process", "outputs", "diagnostics"]) {
    assert.match(page, new RegExp(`data-route="${route}"`));
  }
  assert.match(page, /css\/tailwind\.css/);
  assert.match(page, /id="app-content"/);
});

test("snapshot rendering opens the Book Library and keeps discovery and brand data in the bridge response", () => {
  const { messageHandler, status, content, brandSelect, messages } = loadBridge();

  assert.deepEqual(messages.map((message) => message.command), ["app.ping", "app.refresh"]);
  messageHandler({
    data: {
      version: 1,
      id: "refresh-1",
      ok: true,
      command: "app.snapshot",
      payload: {
        discovery: { paths: { root: { value: "D:/PrintableBook" } }, brands: [{ name: "Amazon" }], books: [{ name: "Book 001" }] },
        globalSettings: { maximumPageConcurrency: 6, dpi: 300 }
      }
    }
  });

  assert.equal(status.textContent, "Connected");
  assert.match(brandSelect.innerHTML, /Amazon/);
  assert.match(content.innerHTML, /1 Books match the active filters/);
  assert.match(content.innerHTML, /Book 001/);
  assert.match(content.innerHTML, /Process Interior/);
  assert.doesNotMatch(content.innerHTML, /Paths \(Read Only\)/);
});

test("Books display the total local folder size with the Interior page count", () => {
  const { messageHandler, content } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-size", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, interiorSourcePageCount: 22, folderSizeBytes: 1610612736, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [] }]
  } } });

  assert.match(content.innerHTML, /22 \/ 22 Interior active · 1\.5 GB/);
  assert.match(readFileSync(appScriptPath, "utf8"), /Math\.round\(value \/ 1024\)/);
});

test("books toolbar starts one confirmed cache cleanup and polls it", () => {
  const { messageHandler, contentListeners, messages, intervals } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: { discovery: { brands: [], books: [] }, globalSettings: {}, bookSummaries: [] } } });

  const clear = { dataset: { action: "clear-cache" }, closest: () => clear };
  contentListeners.click({ target: clear });
  assert.equal(messages.at(-1).command, "cache.clear");

  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.task", payload: { taskId: "cleanup", kind: "CacheCleanup", state: "Running" } } });
  assert.match(messages.at(-1).command, /cache\.clear/);
  intervals.at(-1)();
  assert.equal(messages.at(-1).command, "task.get");
});

test("books toolbar does not start cleanup when confirmation is declined", () => {
  const { messageHandler, contentListeners, messages, browserWindow } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: { discovery: { brands: [], books: [] }, globalSettings: {}, bookSummaries: [] } } });
  browserWindow.confirm = () => false;

  const clear = { dataset: { action: "clear-cache" }, closest: () => clear };
  contentListeners.click({ target: clear });

  assert.notEqual(messages.at(-1).command, "cache.clear");
});

test("completed cache cleanup fetches its result once, shows the summary, and refreshes the library", () => {
  const { messageHandler, status, messages } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: { discovery: { brands: [], books: [] }, globalSettings: {}, bookSummaries: [] } } });
  messageHandler({ data: { version: 1, id: "cleanup", ok: true, command: "background.task", payload: { taskId: "cleanup", kind: "CacheCleanup", state: "Completed" } } });
  assert.equal(messages.at(-1).command, "cache.clear.result");

  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "cache.cleanup.result", payload: { scannedBooks: 10, cleanedBooks: 8, skippedBooks: 2, failedBooks: 0, freedBytes: 4509715660, books: [] } } });
  assert.match(status.textContent, /Cleared 8 Books/);
  assert.match(status.textContent, /Freed/);
  assert.match(status.textContent, /2 skipped/);
  assert.equal(messages.at(-1).command, "app.refresh");
});

test("clear cache is disabled while processing or library refresh is active", () => {
  const { messageHandler, content } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: { discovery: { brands: [], books: [] }, globalSettings: {}, bookSummaries: [] } } });
  messageHandler({ data: { version: 1, id: "process", ok: true, command: "process.snapshot", payload: { isActive: true, isCancelling: false } } });
  assert.match(content.innerHTML, /data-action="clear-cache" disabled/);

  messageHandler({ data: { version: 1, id: "refresh", ok: true, command: "background.task", payload: { taskId: "refresh", kind: "LibraryRefresh", state: "Running" } } });
  assert.match(content.innerHTML, /data-action="clear-cache" disabled/);
});

test("cache cleanup active refresh error does not replace a usable library with failure UI", () => {
  const { messageHandler, content, contentListeners } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: { discovery: { brands: [], books: [{ name: "Book 001" }] }, globalSettings: {}, bookSummaries: [] } } });
  const refresh = { dataset: { action: "refresh" }, closest: () => refresh };
  contentListeners.click({ target: refresh });
  messageHandler({ data: { version: 1, id: "request-1", ok: false, error: "cache_cleanup_active" } });

  assert.match(content.innerHTML, /Book 001/);
  assert.doesNotMatch(content.innerHTML, /Unable to load library/);
});

test("startup shows a loading library view while the first application refresh is pending", () => {
  const { content, messages } = loadBridge();

  assert.deepEqual(messages.map((message) => message.command), ["app.ping", "app.refresh"]);
  assert.match(content.innerHTML, /Loading library…/);
  assert.match(content.innerHTML, /Discovering Books, workspace state and local outputs/);
});

test("manual refresh keeps the existing Books visible and does not enqueue a duplicate frontend refresh", () => {
  const { messageHandler, content, contentListeners, messages, refreshButton } = loadBridge();
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] }, globalSettings: {}, bookSummaries: []
  } } });

  const refresh = { dataset: { action: "refresh" }, closest: () => refresh };
  contentListeners.click({ target: refresh });
  assert.match(content.innerHTML, /Book 001/);
  assert.match(content.innerHTML, /disabled>Refreshing…/);
  assert.equal(refreshButton.disabled, true);
  assert.equal(refreshButton.textContent, "Refreshing…");
  assert.equal(refreshButton.attributes["aria-busy"], "true");
  const messageCount = messages.length;

  contentListeners.click({ target: refresh });
  assert.equal(messages.length, messageCount);
});

test("a refresh failure preserves the existing snapshot and offers a retry", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge();
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] }, globalSettings: {}, bookSummaries: []
  } } });

  const refresh = { dataset: { action: "refresh" }, closest: () => refresh };
  contentListeners.click({ target: refresh });
  messageHandler({ data: { version: 1, id: "request-1", ok: false, error: "app_refresh_failed: source folder is unavailable" } });

  assert.match(content.innerHTML, /Book 001/);
  assert.match(content.innerHTML, /Refresh failed/);
  assert.match(content.innerHTML, /Retry/);
  const messageCount = messages.length;
  contentListeners.click({ target: refresh });
  assert.equal(messages.length, messageCount + 1);
  assert.equal(messages.at(-1).command, "app.refresh");
});

test("an initial refresh failure shows a retryable load failure panel", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge();
  messageHandler({ data: { version: 1, id: "request-1", ok: false, error: "app_refresh_failed: root is unavailable" } });

  assert.match(content.innerHTML, /Unable to load library/);
  assert.match(content.innerHTML, /root is unavailable/);
  const retry = { dataset: { action: "refresh" }, closest: () => retry };
  contentListeners.click({ target: retry });
  assert.equal(messages.at(-1).command, "app.refresh");
});

test("phase 4 page markup includes the interior-only processing workflow", () => {
  const script = readFileSync(appScriptPath, "utf8");

  for (const state of ["Selected queue", "Process Interior", "Published outputs", "Workspace logs", "Settings saved", "Brand settings", "Interior processing", "Current stage", "Elapsed"]) {
    assert.match(script, new RegExp(state));
  }
  assert.match(script, /send\("settings\.save"/);
  assert.match(script, /send\("brand\.settings\.save"/);
  assert.match(script, /send\("book\.validate"/);
  assert.match(script, /send\("process\.start"/);
  assert.match(script, /mode: "interior-only"/);
  assert.match(script, /send\("process\.cancel"/);
  assert.match(script, /send\("app\.refresh"/);
  assert.match(script, /"Interrupted"/);
});

test("saved brand settings survive the application refresh", () => {
  const { messageHandler, contentListeners, messages, brandSettingsEditor, content } = loadBridge("brands");
  const oldJson = "{\"frame\":false}";
  const newJson = "{\"frame\":true}";

  messageHandler({ data: { version: 1, id: "initial-refresh", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [{ name: "Brand One", assets: [] }], books: [] }, globalSettings: {}, bookSummaries: []
  } } });
  messageHandler({ data: { version: 1, id: "brand-get", ok: true, command: "brand.settings", payload: oldJson } });
  brandSettingsEditor.value = newJson;
  contentListeners.input({ target: brandSettingsEditor });

  const save = { dataset: { action: "save-brand-settings" }, closest: () => save };
  contentListeners.click({ target: save });

  const saveRequest = messages.at(-1);
  assert.equal(saveRequest.command, "brand.settings.save");
  assert.equal(saveRequest.payload.json, newJson);

  messageHandler({ data: { version: 1, id: saveRequest.id, ok: true, command: "brand.settings.saved", payload: newJson } });
  assert.equal(messages.at(-1).command, "app.refresh");

  messageHandler({ data: { version: 1, id: "refresh-result", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [{ name: "Brand One", assets: [] }], books: [] }, globalSettings: {}, bookSummaries: []
  } } });

  assert.match(content.innerHTML, /frame.*true/);
});

test("active processing is polled globally and stops after a terminal snapshot", () => {
  const { messageHandler, content, intervals, messages } = loadBridge("books");

  assert.equal(intervals.length, 1);
  messageHandler({ data: { version: 1, id: "process-1", ok: true, command: "process.snapshot", payload: { isActive: true, isCancelling: false } } });
  assert.match(content.innerHTML, /Loading library/);
  intervals[0]();
  assert.equal(messages.at(-1).command, "process.get");

  messageHandler({ data: { version: 1, id: "process-2", ok: true, command: "process.snapshot", payload: { isActive: false, isCancelling: false, currentStep: "Completed" } } });
  const messageCount = messages.length;
  intervals[0]();
  assert.equal(messages.length, messageCount);
});

test("cancelling processing continues global polling even when it is no longer active", () => {
  const { messageHandler, intervals, messages } = loadBridge("books");

  messageHandler({ data: { version: 1, id: "process-1", ok: true, command: "process.snapshot", payload: { isActive: false, isCancelling: true } } });
  intervals[0]();
  assert.equal(messages.at(-1).command, "process.get");
});

test("process controls disable cancellation while a stop is in progress", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("process");

  messageHandler({ data: { version: 1, id: "process-1", ok: true, command: "process.snapshot", payload: { isActive: true, isCancelling: true, currentStep: "Cancelling" } } });
  assert.match(content.innerHTML, /Stopping processing…/);
  assert.match(content.innerHTML, /button class="button-danger" disabled/);

  messageHandler({ data: { version: 1, id: "process-2", ok: true, command: "process.snapshot", payload: { isActive: true, isCancelling: false, currentStep: "Running" } } });
  assert.doesNotMatch(content.innerHTML, /Start Interior Processing/);
  const cancel = { dataset: { action: "cancel-process" }, closest: () => cancel };
  contentListeners.click({ target: cancel });
  assert.equal(messages.at(-1).command, "process.cancel");
});

for (const [status, serializedStatus, detail] of [["Completed", 4, null], ["Failed", 2, "PDF export failed"], ["Cancelled", 3, "Cancelled"]]) {
  test(`${status.toLowerCase()} terminal processing remains visible and allows a new session`, () => {
    const { messageHandler, content, intervals, messages } = loadBridge("process");
    messageHandler({ data: { version: 1, id: `process-${status}`, ok: true, command: "process.snapshot", payload: {
      isActive: false,
      isCancelling: false,
      currentStep: null,
      queue: [{ bookId: { value: "Book 001" }, status: serializedStatus, detail }]
    } } });

    assert.match(content.innerHTML, /Last Interior Processing session/);
    assert.match(content.innerHTML, /Last session/);
    assert.match(content.innerHTML, new RegExp(status));
    assert.doesNotMatch(content.innerHTML, /Selected queue/);
    assert.match(content.innerHTML, /Start New Interior Processing/);
    const messageCount = messages.length;
    intervals[0]();
    assert.equal(messages.length, messageCount);
  });
}

test("a new active snapshot replaces the terminal process session display", () => {
  const { messageHandler, content } = loadBridge("process");
  messageHandler({ data: { version: 1, id: "process-completed", ok: true, command: "process.snapshot", payload: {
    isActive: false, isCancelling: false, queue: [{ bookId: { value: "Book 001" }, status: "Completed", detail: null }]
  } } });
  messageHandler({ data: { version: 1, id: "process-running", ok: true, command: "process.snapshot", payload: {
    isActive: true, isCancelling: false, currentStep: "Processing", queue: [{ bookId: { value: "Book 002" }, status: "Running", detail: "Processing" }]
  } } });

  assert.match(content.innerHTML, /Active Interior Processing session/);
  assert.match(content.innerHTML, /Book 002/);
  assert.doesNotMatch(content.innerHTML, /Last session/);
});

test("a mixed terminal queue retains the most severe terminal step", () => {
  const { messageHandler, content } = loadBridge("process");
  messageHandler({ data: { version: 1, id: "process-mixed", ok: true, command: "process.snapshot", payload: {
    isActive: false,
    isCancelling: false,
    currentStep: null,
    queue: [
      { bookId: { value: "Book completed" }, status: 4, detail: null },
      { bookId: { value: "Book failed" }, status: 2, detail: "PDF export failed" }
    ]
  } } });

  assert.match(content.innerHTML, /Book failed/);
  assert.match(content.innerHTML, /<strong>Last session<\/strong><span>Failed<\/span>/);
  assert.match(content.innerHTML, /Run needs review/);
  assert.match(content.innerHTML, /PDF export failed/);
});

test("book filters render a recovered interrupted workspace without failing", () => {
  const { messageHandler, content } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, workspaceStatus: 5, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [] }]
  } } });

  assert.match(content.innerHTML, /Book 001/);
  assert.match(content.innerHTML, /Preflight/);
});

test("book detail renders frame mode truth and sends per-image overrides through the bridge", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("books");
  const snapshot = (frameMode) => ({
    discovery: {
      paths: { root: { value: "D:/PrintableBook" } },
      brands: [],
      books: [{ id: { value: "Book 001" }, name: "Book 001", directory: { value: "D:/PrintableBook/sources/Book 001" } }]
    },
    globalSettings: {},
    bookSummaries: [{
      bookId: { value: "Book 001" },
      workspaceStatus: "Not started",
      validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [],
      interiorSourcePageCount: 1,
      activeInteriorSourcePageCount: 0,
      hasBackground: true,
      assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", width: 2550, height: 2550, frameMode, isActive: false }]
    }]
  });

  messageHandler({ data: { version: 1, id: "book-1", ok: true, command: "app.snapshot", payload: snapshot(0) } });

  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: assetsTab });

  assert.match(content.innerHTML, /Interior assets/);
  assert.match(content.innerHTML, /Use Brand background/);
  assert.match(content.innerHTML, /0 of 1 active/);
  assert.match(content.innerHTML, /set-interior-active/);
  assert.match(content.innerHTML, /is-inactive/);
  assert.match(content.innerHTML, /Choose exactly which pages will be processed/);
  assert.match(content.innerHTML, /option value="auto" selected/);
  const messageCountBeforeEdit = messages.length;
  contentListeners.change({ target: { dataset: { action: "set-book-background", bookId: "Book 001" }, checked: false } });
  contentListeners.change({ target: { dataset: { action: "set-interior-active", bookId: "Book 001", sourceReference: "Book interior/page-001.png" }, checked: true } });
  contentListeners.change({ target: { dataset: { action: "set-interior-frame-mode", bookId: "Book 001", sourceReference: "Book interior/page-001.png" }, value: "enabled" } });
  assert.equal(messages.length, messageCountBeforeEdit);

  messageHandler({ data: { version: 1, id: "book-2", ok: true, command: "app.snapshot", payload: snapshot(1) } });
  assert.match(content.innerHTML, /option value="enabled" selected/);
});

test("Book Interior edits stay local until one explicit save request", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("books");
  const snapshot = {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{
      bookId: { value: "Book 001" }, workspaceStatus: "Not started", validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [],
      interiorSourcePageCount: 1, activeInteriorSourcePageCount: 1, hasBackground: false,
      assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", width: 2550, height: 2550, frameMode: "auto", isActive: true }]
    }]
  };

  messageHandler({ data: { version: 1, id: "book-draft", ok: true, command: "app.snapshot", payload: snapshot } });
  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: assetsTab });
  const messageCountBeforeEdit = messages.length;

  contentListeners.change({ target: { dataset: { action: "set-book-background", bookId: "Book 001" }, checked: true } });
  contentListeners.change({ target: { dataset: { action: "set-interior-active", bookId: "Book 001", sourceReference: "Book interior/page-001.png" }, checked: false } });
  contentListeners.change({ target: { dataset: { action: "set-interior-frame-mode", bookId: "Book 001", sourceReference: "Book interior/page-001.png" }, value: "enabled" } });

  assert.equal(messages.length, messageCountBeforeEdit, "editing must not refresh the Book drawer");
  assert.match(content.innerHTML, /data-action="save-book-interior-settings"/);
  assert.match(content.innerHTML, /Unsaved changes/);

  const save = { dataset: { action: "save-book-interior-settings", bookId: "Book 001" }, closest: () => save };
  contentListeners.click({ target: save });
  assert.deepEqual(messages.at(-1), {
    version: 1,
    id: "request-1",
    command: "book.interior.settings.save",
    payload: {
      bookId: "Book 001",
      hasBackground: true,
      assets: [{ sourceReference: "Book interior/page-001.png", active: false, frameMode: "enabled" }]
    }
  });
});

test("Books render direct Cover and Interior local image URLs and replace a failed image locally", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-asset", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, representativeCoverReference: "Book cover/cover.png", validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [], assets: [
      { sourceReference: "Book cover/cover.png", localImageUrl: "file:///D:/Printable%20Book/Cover%20%231%20%25.png", relativePath: "Book cover/cover.png", fileName: "cover.png", folder: "Book cover", kind: "Cover", width: 2550, height: 2550, frameMode: "auto" },
      { sourceReference: "Book interior/page-001.png", localImageUrl: "file:///D:/Printable%20Book/B%E1%BB%99%20s%C3%A1ch%20%231%20%25/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", width: 2550, height: 2550, frameMode: "auto" }
    ] }]
  } } });

  assert.match(content.innerHTML, /src="file:\/\/\/D:\/Printable%20Book\/Cover%20%231%20%25\.png"/);
  assert.match(content.innerHTML, /width="256" height="256" loading="lazy" decoding="async" data-local-image/);

  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  assert.match(content.innerHTML, /src="file:\/\/\/D:\/Printable%20Book\/Cover%20%231%20%25\.png"/);
  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: assetsTab });
  assert.match(content.innerHTML, /Interior assets/);
  assert.match(content.innerHTML, /page-001\.png/);
  assert.match(content.innerHTML, /src="file:\/\/\/D:\/Printable%20Book/);
  assert.match(content.innerHTML, /loading="lazy" decoding="async" data-local-image/);
  assert.equal(messages.some((message) => message.command.includes("preview")), false);

  let fallback;
  contentListeners.error({ target: { matches: (selector) => selector === "img[data-local-image]", dataset: { imageFallback: "Image unavailable" }, replaceWith: (replacement) => { fallback = replacement; } } });
  assert.equal(fallback.className, "book-preview-fallback");
  assert.equal(fallback.textContent, "Image unavailable");
});

test("frontend source contains no removed asset-preview bridge protocol", () => {
  const script = readFileSync(appScriptPath, "utf8");

  for (const removedProtocol of ["asset.preview.request", "asset.preview.get", "asset.preview.result", "asset.preview.error"]) {
    assert.equal(script.includes(removedProtocol), false, `Removed protocol remains: ${removedProtocol}`);
  }
});

test("validation limits Book detail to Interior preflight while cover work is deferred", () => {
  const { messageHandler, content, contentListeners } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-validation", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{
      bookId: { value: "Book 001" }, sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [],
      validationChecks: [{ code: "book.interior_ready", message: "Interior source images were discovered.", isSuccess: true }, { code: "book.cover_skipped", message: "Cover is unavailable and will be skipped for Interior-only processing.", isSuccess: true, isWarning: true }],
      fullBookValidationChecks: [{ code: "book.interior_ready", message: "Interior source images were discovered.", isSuccess: true }, { code: "book.cover_required", message: "A Cover PNG is required before this Book can be exported as a full book.", isSuccess: false }]
    }]
  } } });

  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const validationTab = { dataset: { action: "book-tab", bookTab: "validation" }, closest: () => validationTab };
  contentListeners.click({ target: validationTab });
  assert.match(content.innerHTML, /Interior-only preflight checks the source pages that will be processed/);
  assert.match(content.innerHTML, /Interior source images were discovered/);
  assert.doesNotMatch(content.innerHTML, /Cover is unavailable/);
});

test("PDF Library shows only Completed Books that currently have output", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });

  assert.match(content.innerHTML, /data-pdf-book-id="Book Alpha"/);
  assert.match(content.innerHTML, /data-pdf-book-id="Book Delta"/);
  assert.doesNotMatch(content.innerHTML, /data-pdf-book-id="Book Beta"/);
  assert.doesNotMatch(content.innerHTML, /data-pdf-book-id="Book Gamma"/);
});

test("PDF Library groups current Cover and Interior PDFs under one Book", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });

  const alphaStart = content.innerHTML.indexOf("Book Alpha");
  const deltaStart = content.innerHTML.indexOf("Book Delta");
  const alphaMarkup = content.innerHTML.slice(alphaStart, deltaStart > alphaStart ? deltaStart : undefined);

  assert.match(alphaMarkup, /Book Alpha - Interior\.pdf/);
  assert.match(alphaMarkup, /Book Alpha - Cover\.pdf/);
  assert.match(alphaMarkup, /Open PDF/);
  assert.match(alphaMarkup, /Reveal in Explorer/);
  assert.match(alphaMarkup, /Copy path/);
});

test("PDF Library output actions send a book-scoped command", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "output-1", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });
  assert.match(content.innerHTML, /Book Alpha - Interior\.pdf/);
  assert.match(content.innerHTML, /Reveal in Explorer/);
  const artifactReference = "D:\\PrintableBook\\sources\\Book Alpha\\Output\\Book Alpha - Interior.pdf";
  const open = { dataset: { action: "open-output", bookId: "Book Alpha", artifactReference }, closest: () => open };
  contentListeners.click({ target: open });
  assert.deepEqual(messages.at(-1), { version: 1, id: "request-1", command: "book.output.open", payload: { bookId: "Book Alpha", artifactReference } });
});

test("Diagnostics route requests and renders sanitized responsiveness events", () => {
  const { messageHandler, content, routeButtons, messages } = loadBridge("diagnostics");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [] }, globalSettings: {}, bookSummaries: []
  } } });
  routeButtons.find((button) => button.dataset.route === "diagnostics").listeners.click();
  assert.deepEqual(messages.slice(-2).map((message) => message.command), ["diagnostics.get", "task.list"]);
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "diagnostics.snapshot", payload: [{ timestamp: "2026-08-25T00:00:00Z", severity: "Slow", kind: "dispatcher.stall", operation: "dispatcher", durationMilliseconds: 300, subject: null, activeOperations: ["book.scan (Book 001)"] }] } });
  assert.match(content.innerHTML, /UI responsiveness/);
  assert.match(content.innerHTML, /Slow/);
  assert.match(content.innerHTML, /Active during stall/);
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.tasks", payload: [{ kind: "LibraryRefresh", state: "Running", subject: "Library", step: "discovery" }] } });
  assert.match(content.innerHTML, /Background workers/);
  assert.match(content.innerHTML, /LibraryRefresh/);
});
