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
  const searchInput = { focused: false, selection: null, focus() { this.focused = true; }, setSelectionRange(start, end) { this.selection = [start, end]; } };
  const content = {
    innerHTML: "",
    addEventListener: (eventName, handler) => { contentListeners[eventName] = handler; },
    insertAdjacentHTML: (_position, markup) => { content.innerHTML += markup; },
    querySelector: (selector) => selector === '[data-action="pdf-library-search"]' ? searchInput : null
  };
  const brandSelectListeners = {};
  const brandSelect = { innerHTML: "", value: "", addEventListener: (eventName, handler) => { brandSelectListeners[eventName] = handler; } };
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

  return { messageHandler, status, content, brandSelect, brandSelectListeners, brandSettingsEditor, refreshButton, contentListeners, documentListeners, routeButtons, intervals, messages, browserWindow, searchInput };
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

const completedPdfBook = (index, {
  generatedAt = `2026-08-${String(Math.min(26, index + 1)).padStart(2, "0")}T10:00:00Z`,
  fileSizeBytes = index * 1024 * 1024,
  coverUrl = `file:///D:/PrintableBook/sources/Book%20${index}/Book%20cover/cover.png`
} = {}) => ({
  book: { id: { value: `Book ${String(index).padStart(2, "0")}` }, name: `Book ${String(index).padStart(2, "0")}` },
  summary: {
    bookId: { value: `Book ${String(index).padStart(2, "0")}` }, workspaceStatus: "Completed", lastRunAt: generatedAt,
    representativeCoverReference: `D:\\PrintableBook\\sources\\Book ${index}\\Book cover\\cover.png`,
    assets: [{ sourceReference: `D:\\PrintableBook\\sources\\Book ${index}\\Book cover\\cover.png`, relativePath: "Book cover/cover.png", fileName: "cover.png", folder: "Book cover", kind: "Cover", width: 2588, height: 2625, frameMode: "auto", localImageUrl: coverUrl, isActive: true }],
    outputSummaries: [{ artifactReference: `D:\\PrintableBook\\sources\\Book ${index}\\Output\\Book ${index} - Interior.pdf`, fileName: `Book ${index} - Interior.pdf`, verificationStatus: "Verified", generatedAt, pageCount: 40 + index, widthInches: 8.5, heightInches: 8.5, fileSizeBytes }]
  }
});

const manyPdfLibrarySnapshot = (count = 25) => {
  const items = Array.from({ length: count }, (_, position) => completedPdfBook(position + 1));
  return { discovery: { brands: [], books: items.map((item) => item.book) }, globalSettings: {}, bookSummaries: items.map((item) => item.summary) };
};

const diagnosticsSnapshot = () => ({
  discovery: {
    brands: [],
    books: [{ id: { value: "Book Alpha" }, name: "Book Alpha" }]
  },
  globalSettings: {},
  bookSummaries: [{
    bookId: { value: "Book Alpha" },
    workspaceStatus: "Completed",
    currentStep: null,
    lastRunAt: "2026-08-25T23:34:07Z",
    sourceFolders: [
      { name: "Book colored", status: "Present", imageCount: 2 },
      { name: "Book cover", status: "Present", imageCount: 2 },
      { name: "Book interior", status: "Present", imageCount: 44 },
      { name: "Source cover", status: "Present", imageCount: 5 },
      { name: "Source cover colored", status: "Missing", imageCount: 0 }
    ],
    logs: []
  }]
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

test("desktop navigation names the outputs route PDF Library", () => {
  const page = readFileSync(join(process.cwd(), "src", "PrintableBook.Desktop", "Frontend", "index.html"), "utf8");

  assert.match(page, /data-route="outputs"[^>]*>[\s\S]*?<span>PDF Library<\/span>/);
  assert.doesNotMatch(page, /data-route="outputs"[^>]*>[\s\S]*?<span>Outputs<\/span>/);
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

test("Books display the active Interior page count without local folder size", () => {
  const { messageHandler, content } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-size", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, interiorSourcePageCount: 22, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [] }]
  } } });

  assert.match(content.innerHTML, /22 \/ 22 Interior active/);
  assert.doesNotMatch(content.innerHTML, /Interior active ·/);
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

  for (const state of ["Selected queue", "Process Interior", "Settings saved", "Brand settings", "Interior processing", "Current stage", "Elapsed", "Interior settings"]) {
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

test("a delayed process status response cannot replace a newly started session", () => {
  const { messageHandler, content } = loadBridge("process");

  messageHandler({ data: { version: 1, id: "process-start", ok: true, command: "process.snapshot", payload: {
    isActive: true, isCancelling: false, startedAt: "2026-08-26T12:00:01Z", currentStep: "Preparing",
    queue: [{ bookId: { value: "Book new" }, status: "Running", detail: "Preparing" }]
  } } });
  messageHandler({ data: { version: 1, id: "process-get-stale", ok: true, command: "process.snapshot", payload: {
    isActive: false, isCancelling: false, startedAt: "2026-08-26T11:00:00Z", currentStep: "Completed",
    queue: [{ bookId: { value: "Book previous" }, status: "Completed", detail: null }]
  } } });

  assert.match(content.innerHTML, /Book new/);
  assert.doesNotMatch(content.innerHTML, /Book previous/);
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

test("book detail renders only the summary and paginated Interior settings", () => {
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
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });

  assert.match(content.innerHTML, /Interior settings/);
  assert.match(content.innerHTML, /Use Brand background/);
  assert.match(content.innerHTML, /Intro pages/);
  assert.match(content.innerHTML, /data-intro-total-pages/);
  assert.doesNotMatch(content.innerHTML, /data-action="set-interior-active"/);
  const messageCountBeforeEdit = messages.length;
  contentListeners.change({ target: { dataset: { action: "set-book-background", bookId: "Book 001" }, checked: false } });
  assert.equal(messages.length, messageCountBeforeEdit);

  messageHandler({ data: { version: 1, id: "book-2", ok: true, command: "app.snapshot", payload: snapshot(1) } });
  assert.match(content.innerHTML, /Interior settings/);
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
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });
  const messageCountBeforeEdit = messages.length;

  contentListeners.change({ target: { dataset: { action: "set-book-background", bookId: "Book 001" }, checked: true } });

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
      assets: []
    }
  });
});

test("Book detail configures an ordered custom Intro selection from Book interior", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "intro-draft", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [{ name: "Demo", introTemplateAssets: [
      { key: "first.png", fileName: "first.png", sourceReference: "brand/IntroTemplate/first.png", localImageUrl: "file:///first.png" },
      { key: "second.png", fileName: "second.png", sourceReference: "brand/IntroTemplate/second.png", localImageUrl: "file:///second.png" }
    ] }], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, hasIntro: false, selectedIntroInteriorSourceKeys: [], validationChecks: [], sourceFolders: [{ name: "Book interior" }], publishedArtifacts: [], interiorPages: [], logs: [], assets: [
      { sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", frameMode: "auto", isActive: true, localImageUrl: "file:///page-001.png" },
      { sourceReference: "Book interior/page-003.png", relativePath: "Book interior/page-003.png", fileName: "page-003.png", folder: "Book interior", kind: "Interior", frameMode: "enabled", isActive: false, localImageUrl: "file:///page-003.png" }
    ] }]
  } } });
  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });
  assert.match(content.innerHTML, /Intro pages/);
  assert.match(content.innerHTML, /Automatic/);

  contentListeners.change({ target: { dataset: { action: "set-intro-mode", bookId: "Book 001" }, value: "custom" } });
  const add = { dataset: { action: "intro-add-template", bookId: "Book 001", introSourceReference: "Book interior/page-003.png" }, closest: () => add };
  contentListeners.click({ target: add });
  assert.match(content.innerHTML, /Intro #1/);
  const save = { dataset: { action: "save-book-interior-settings", bookId: "Book 001" }, closest: () => save };
  contentListeners.click({ target: save });

  assert.deepEqual(messages.at(-1).payload, { bookId: "Book 001", hasIntro: true, introSourceReferences: ["Book interior/page-003.png"], assets: [] });
});

test("Interior settings pages Intro templates six cards at a time", () => {
  const { messageHandler, content, contentListeners } = loadBridge("books");
  const templates = Array.from({ length: 7 }, (_, index) => ({ key: `intro-${index + 1}.png`, fileName: `intro-${index + 1}.png`, localImageUrl: `file:///intro-${index + 1}.png` }));
  messageHandler({ data: { version: 1, id: "intro-page", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [{ name: "Demo", introTemplateAssets: templates }], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, hasIntro: false, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [], assets: [] }]
  } } });
  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });

  assert.match(content.innerHTML, /intro-6\.png/);
  assert.doesNotMatch(content.innerHTML, /intro-7\.png/);
  assert.match(content.innerHTML, /1–6 of 7/);

  const next = { dataset: { action: "intro-template-page", introTemplatePage: "next" }, closest: () => next };
  contentListeners.click({ target: next });
  assert.match(content.innerHTML, /intro-7\.png/);
  assert.match(content.innerHTML, /Page 2 of 2/);
});

test("Adding to a persisted custom Intro submits asset source references instead of stored source keys", () => {
  const { messageHandler, contentListeners, messages } = loadBridge("books");
  const firstReference = "D:\\PrintableBook\\sources\\Book 001\\Book interior\\page-001.png";
  const secondReference = "D:\\PrintableBook\\sources\\Book 001\\Book interior\\page-002.png";
  messageHandler({ data: { version: 1, id: "persisted-custom-intro", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{
      bookId: { value: "Book 001" }, hasIntro: true, selectedIntroInteriorSourceKeys: ["Book interior/page-001.png"], validationChecks: [], sourceFolders: [{ name: "Book interior" }], publishedArtifacts: [], interiorPages: [], logs: [],
      interiorSourcePages: [
        { sourceReference: firstReference, sourceKey: "Book interior/page-001.png", frameMode: "auto", isActive: true },
        { sourceReference: secondReference, sourceKey: "Book interior/page-002.png", frameMode: "auto", isActive: true }
      ],
      assets: [
        { sourceReference: firstReference, relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", frameMode: "auto", isActive: true },
        { sourceReference: secondReference, relativePath: "Book interior/page-002.png", fileName: "page-002.png", folder: "Book interior", kind: "Interior", frameMode: "auto", isActive: true }
      ]
    }]
  } } });

  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });
  const add = { dataset: { action: "intro-add-template", bookId: "Book 001", introSourceReference: secondReference }, closest: () => add };
  contentListeners.click({ target: add });
  const save = { dataset: { action: "save-book-interior-settings", bookId: "Book 001" }, closest: () => save };
  contentListeners.click({ target: save });

  assert.deepEqual(messages.at(-1).payload, { bookId: "Book 001", introSourceReferences: [firstReference, secondReference], assets: [] });
});

test("Brand switching leaves custom Book interior Intro selection and readiness unchanged", () => {
  const { messageHandler, content, contentListeners, brandSelect, brandSelectListeners } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "brand-switch", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [
      { name: "Brand A", introTemplateAssets: [{ key: "shared.png", fileName: "shared.png", localImageUrl: "file:///shared.png" }] },
      { name: "Brand B", introTemplateAssets: [{ key: "other.png", fileName: "other.png", localImageUrl: "file:///other.png" }] }
    ], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, validationStatus: "Ready", hasIntro: true, selectedIntroInteriorSourceKeys: ["Book interior/page-001.png"], validationChecks: [], sourceFolders: [{ name: "Book interior" }], publishedArtifacts: [], interiorPages: [], logs: [], assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", frameMode: "auto", isActive: true, localImageUrl: "file:///page-001.png" }] }]
  } } });
  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });
  assert.match(content.innerHTML, /Custom Book interior/);
  assert.match(content.innerHTML, /Intro #1/);

  brandSelect.value = "Brand B";
  brandSelectListeners.change();

  assert.match(content.innerHTML, /Custom Book interior/);
  assert.match(content.innerHTML, /Intro #1/);
  assert.doesNotMatch(content.innerHTML, /missing from the current Brand/);
});

test("Automatic Intro template preview dimensions gate the current Brand readiness without sending a bridge request", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "intro-dimensions", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [{ name: "Demo", introTemplateAssets: [{ key: "intro.png", fileName: "intro.png", localImageUrl: "file:///intro.png" }] }], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, validationStatus: "Ready", hasIntro: false, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [], assets: [] }]
  } } });
  const open = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => open };
  contentListeners.click({ target: open });
  const settings = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settings };
  contentListeners.click({ target: settings });
  const messageCount = messages.length;

  contentListeners.load({ target: { matches: (selector) => selector === "img[data-local-image]", dataset: { introTemplateId: "Demo%00intro.png" }, naturalWidth: 1000, naturalHeight: 1000 } });

  assert.match(content.innerHTML, /must be 1024 × 1024 or 2048 × 2048 pixels/);
  assert.match(content.innerHTML, /Needs review/);
  assert.equal(messages.length, messageCount);
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
  const settingsTab = { dataset: { action: "book-tab", bookTab: "settings" }, closest: () => settingsTab };
  contentListeners.click({ target: settingsTab });
  assert.match(content.innerHTML, /Interior settings/);
  assert.match(content.innerHTML, /Use Brand background/);
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

test("Book detail keeps the Interior preflight action while cover work is deferred", () => {
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
  assert.match(content.innerHTML, /Run Interior preflight/);
  assert.doesNotMatch(content.innerHTML, /data-book-tab="validation"/);
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
  assert.match(alphaMarkup, />Open</);
  assert.match(alphaMarkup, />Reveal</);
  assert.match(alphaMarkup, />Copy</);
});

test("PDF Library uses Book-centric copy and removes run history language", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });

  assert.match(content.innerHTML, /PDF Library/);
  assert.match(content.innerHTML, /Completed Books with local PDF output/);
  assert.doesNotMatch(content.innerHTML, /Latest outputs/i);
  assert.doesNotMatch(content.innerHTML, /Previous runs/i);
  assert.doesNotMatch(content.innerHTML, /before publishing/i);
});

test("PDF Library renders one top-level card per eligible Book", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });

  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 2);
  assert.match(content.innerHTML, /data-pdf-book-id="Book Alpha"/);
  assert.match(content.innerHTML, /data-pdf-book-id="Book Delta"/);
});

test("PDF Library renders at most 12 Books on one page", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(25) } });

  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 12);
  assert.match(content.innerHTML, /1–12 of 25/);
  assert.match(content.innerHTML, /Page 1 of 3/);
});

test("PDF Library pagination navigates locally", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(25) } });
  const messageCount = messages.length;
  const navigate = (page) => { const target = { dataset: { action: "pdf-library-page", pdfLibraryPage: page }, closest: () => target }; contentListeners.click({ target }); };

  navigate("next");
  assert.match(content.innerHTML, /13–24 of 25/);
  assert.match(content.innerHTML, /Page 2 of 3/);
  navigate("last");
  assert.match(content.innerHTML, /25–25 of 25/);
  assert.match(content.innerHTML, /Page 3 of 3/);
  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 1);
  navigate("previous");
  assert.match(content.innerHTML, /Page 2 of 3/);
  navigate("first");
  assert.match(content.innerHTML, /1–12 of 25/);
  assert.match(content.innerHTML, /Page 1 of 3/);
  assert.equal(messages.length, messageCount);
});

test("PDF Library search resets and clamps pagination", () => {
  const { messageHandler, content, contentListeners } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(25) } });
  const last = { dataset: { action: "pdf-library-page", pdfLibraryPage: "last" }, closest: () => last };
  contentListeners.click({ target: last });
  contentListeners.input({ target: { dataset: { action: "pdf-library-search" }, value: "Book 01", selectionStart: 7 } });

  assert.match(content.innerHTML, /Page 1 of 1/);
  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 1);
  assert.match(content.innerHTML, /data-pdf-book-id="Book 01"/);
});

test("PDF Library reuses each Book representative cover thumbnail", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(1) } });

  assert.match(content.innerHTML, /file:\/\/\/D:\/PrintableBook\/sources\/Book%201\/Book%20cover\/cover\.png/);
  assert.match(content.innerHTML, /loading="lazy"/);
  assert.match(content.innerHTML, /decoding="async"/);
  assert.match(content.innerHTML, /data-local-image/);
});

test("PDF Library shows the existing fallback when a representative cover is unavailable", () => {
  const { messageHandler, content } = loadBridge("outputs");
  const snapshot = manyPdfLibrarySnapshot(1);
  snapshot.bookSummaries[0].representativeCoverReference = "";
  snapshot.bookSummaries[0].assets = [];
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: snapshot } });

  assert.match(content.innerHTML, /Cover unavailable/);
});

test("PDF Library switches between Grid and List without changing page size", () => {
  const { messageHandler, content, contentListeners } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(13) } });

  assert.match(content.innerHTML, /pdf-library-grid/);
  assert.match(content.innerHTML, /data-pdf-library-view="grid"/);
  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 12);
  const list = { dataset: { action: "pdf-library-view", pdfLibraryView: "list" }, closest: () => list };
  contentListeners.click({ target: list });
  assert.match(content.innerHTML, /pdf-library-list/);
  assert.match(content.innerHTML, /aria-pressed="true"[^>]*>List</);
  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 12);
});

test("PDF Library Grid uses compact Book cards and the documented desktop columns", () => {
  const { messageHandler, content } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(4) } });
  const css = readFileSync(join(process.cwd(), "src", "PrintableBook.Desktop", "Frontend", "css", "book-workspace.css"), "utf8");

  assert.match(content.innerHTML, /pdf-library-page/);
  assert.match(content.innerHTML, /pdf-library-grid-scroll/);
  assert.match(content.innerHTML, /pdf-library-file-status/);
  assert.match(content.innerHTML, /pdf-library-grid/);
  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 4);
  assert.equal(content.innerHTML.match(/pdf-library-book-preview/g)?.length ?? 0, 4);
  assert.match(content.innerHTML, />Open</);
  assert.match(content.innerHTML, />Reveal</);
  assert.match(content.innerHTML, />Copy</);
  assert.match(css, /repeat\(3,minmax\(0,1fr\)\)/);
  assert.match(css, /\.pdf-library-grid \{ display:grid; grid-template-columns:repeat\(3,minmax\(0,1fr\)\); grid-auto-rows:1fr;/);
  assert.match(css, /\.pdf-library-book-grid \{ display:grid; grid-template-rows:auto auto minmax\(0,1fr\); min-width:0; height:100%;/);
  assert.match(css, /\.pdf-library-book-grid \.pdf-library-book-header > div \{ width:100%; min-width:0;/);
  assert.match(css, /\.pdf-library-book-grid \.pdf-library-title-row \{ display:grid; grid-template-columns:minmax\(0,1fr\) auto; min-width:0;/);
  assert.match(css, /\.pdf-library-book-grid \.pdf-library-title-row h2 \{ min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;/);
  assert.match(css, /\.pdf-library-grid-scroll \{ min-height:0; overflow-y:auto;/);
  assert.match(css, /\.pdf-library-file-title \{ display:grid; grid-template-columns:minmax\(0,1fr\) auto;/);
  assert.match(css, /\.pdf-library-book-grid \.pdf-library-file-copy \{ grid-template-rows:20px 18px 32px;/);
  assert.match(css, /@media \(min-width:1450px\) \{ \.pdf-library-grid \{ grid-template-columns:repeat\(4,/);
});

test("PDF Library List uses bounded thumbnails and verbose output actions", () => {
  const { messageHandler, content, contentListeners } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(2) } });
  const list = { dataset: { action: "pdf-library-view", pdfLibraryView: "list" }, closest: () => list };
  contentListeners.click({ target: list });
  const css = readFileSync(join(process.cwd(), "src", "PrintableBook.Desktop", "Frontend", "css", "book-workspace.css"), "utf8");

  assert.match(content.innerHTML, /pdf-library-list/);
  assert.match(content.innerHTML, /Open PDF/);
  assert.match(content.innerHTML, /Reveal in Explorer/);
  assert.match(content.innerHTML, /Copy path/);
  assert.match(css, /\.pdf-library-book-list \{ display:grid; grid-template-columns:96px/);
  assert.match(css, /\.pdf-library-book-list \.pdf-library-book-preview \{ width:96px; height:100%;/);
});

test("PDF Library keeps paging local, preserves it across view changes, and opens an artifact from page two", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(25) } });
  const messageCount = messages.length;
  const click = (action, dataset = {}) => {
    const target = { dataset: { action, ...dataset }, closest: () => target };
    contentListeners.click({ target });
  };

  click("pdf-library-page", { pdfLibraryPage: "next" });
  click("pdf-library-view", { pdfLibraryView: "list" });
  assert.match(content.innerHTML, /Page 2 of 3/);
  assert.match(content.innerHTML, /pdf-library-list/);
  assert.equal(messages.length, messageCount);

  const artifactReference = "D:\\PrintableBook\\sources\\Book 13\\Output\\Book 13 - Interior.pdf";
  click("open-output", { bookId: "Book 13", artifactReference });
  assert.deepEqual(messages.at(-1), { version: 1, id: "request-1", command: "book.output.open", payload: { bookId: "Book 13", artifactReference } });
});

test("PDF Library resets and clamps pagination when sorting, filtering, or refreshing changes its result set", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(25) } });
  const messageCount = messages.length;
  const last = { dataset: { action: "pdf-library-page", pdfLibraryPage: "last" }, closest: () => last };
  contentListeners.click({ target: last });
  assert.match(content.innerHTML, /Page 3 of 3/);

  contentListeners.change({ target: { dataset: { action: "pdf-library-sort" }, value: "name" } });
  assert.match(content.innerHTML, /Page 1 of 3/);
  contentListeners.click({ target: last });
  messageHandler({ data: { version: 1, id: "smaller", ok: true, command: "app.snapshot", payload: manyPdfLibrarySnapshot(10) } });
  assert.match(content.innerHTML, /Page 1 of 1/);
  assert.equal(content.innerHTML.match(/data-pdf-book-id=/g)?.length ?? 0, 10);
  assert.equal(messages.length, messageCount);
});

test("PDF Library search filters by Book name", () => {
  const { messageHandler, content, contentListeners, messages, searchInput } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });
  const messageCount = messages.length;

  contentListeners.input({ target: { dataset: { action: "pdf-library-search" }, value: "Delta", selectionStart: 5 } });

  assert.doesNotMatch(content.innerHTML, /data-pdf-book-id="Book Alpha"/);
  assert.match(content.innerHTML, /data-pdf-book-id="Book Delta"/);
  assert.equal(searchInput.focused, true);
  assert.deepEqual(searchInput.selection, [5, 5]);
  assert.equal(messages.length, messageCount);
});

test("PDF Library search has a distinct no-match empty state", () => {
  const { messageHandler, content, contentListeners } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });

  contentListeners.input({ target: { dataset: { action: "pdf-library-search" }, value: "Does not exist" } });

  assert.match(content.innerHTML, /No PDF Books match your search/);
  assert.doesNotMatch(content.innerHTML, /No completed PDFs yet/);
});

test("PDF Library sorts by newest name and current PDF size", () => {
  const { messageHandler, content, contentListeners } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });
  const firstBook = () => content.innerHTML.match(/data-pdf-book-id="([^"]+)"/)?.[1] ?? "";

  assert.equal(firstBook(), "Book Delta");
  contentListeners.change({ target: { dataset: { action: "pdf-library-sort" }, value: "name" } });
  assert.equal(firstBook(), "Book Alpha");
  contentListeners.change({ target: { dataset: { action: "pdf-library-sort" }, value: "size" } });
  assert.equal(firstBook(), "Book Alpha");
});

test("PDF Library Open PDF sends the exact Book artifact reference", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "output-1", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });
  assert.match(content.innerHTML, /Book Alpha - Interior\.pdf/);
  assert.match(content.innerHTML, />Reveal</);
  const artifactReference = "D:\\PrintableBook\\sources\\Book Alpha\\Output\\Book Alpha - Interior.pdf";
  const open = { dataset: { action: "open-output", bookId: "Book Alpha", artifactReference }, closest: () => open };
  contentListeners.click({ target: open });
  assert.deepEqual(messages.at(-1), { version: 1, id: "request-1", command: "book.output.open", payload: { bookId: "Book Alpha", artifactReference } });
});

test("PDF Library Reveal in Explorer sends the exact Book artifact reference", () => {
  const { messageHandler, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "output-1", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });
  const artifactReference = "D:\\PrintableBook\\sources\\Book Alpha\\Output\\Book Alpha - Cover.pdf";
  const reveal = { dataset: { action: "reveal-output", bookId: "Book Alpha", artifactReference }, closest: () => reveal };
  contentListeners.click({ target: reveal });
  assert.deepEqual(messages.at(-1), { version: 1, id: "request-1", command: "book.output.reveal", payload: { bookId: "Book Alpha", artifactReference } });
});

test("PDF Library Copy path sends the exact Book artifact reference", () => {
  const { messageHandler, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "output-1", ok: true, command: "app.snapshot", payload: pdfLibrarySnapshot() } });
  const artifactReference = "D:\\PrintableBook\\sources\\Book Delta\\Output\\Book Delta - Interior.pdf";
  const copy = { dataset: { action: "copy-output-path", bookId: "Book Delta", artifactReference }, closest: () => copy };
  contentListeners.click({ target: copy });
  assert.deepEqual(messages.at(-1), { version: 1, id: "request-1", command: "book.output.copy-path", payload: { bookId: "Book Delta", artifactReference } });
});

test("Diagnostics opens on Summary with four tabs", () => {
  const { messageHandler, content } = loadBridge("diagnostics");

  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });

  assert.match(content.innerHTML, /role="tablist"/);
  assert.match(content.innerHTML, />Summary</);
  assert.match(content.innerHTML, />Tasks</);
  assert.match(content.innerHTML, />Performance</);
  assert.match(content.innerHTML, />Book</);
  assert.match(content.innerHTML, /data-diagnostics-tab="summary"/);
  assert.match(content.innerHTML, /aria-selected="true"/);
});

test("Diagnostics tab switching is local and sends no bridge message", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("diagnostics");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });
  const count = messages.length;
  const target = { dataset: { action: "diagnostics-tab", diagnosticsTab: "tasks" } };
  target.closest = () => target;

  contentListeners.click({ target });

  assert.match(content.innerHTML, /data-diagnostics-panel="tasks"/);
  assert.equal(messages.length, count);
});

test("Diagnostics Summary shows compact health instead of large tables", () => {
  const { messageHandler, content, browserWindow } = loadBridge("diagnostics");
  browserWindow.uiDiagnostics = [{ timestamp: "2026-08-26T11:21:30Z", severity: 2, kind: "operation", operation: "snapshot.refresh", durationMilliseconds: 4510, subject: null, activeOperations: [] }];

  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });

  assert.match(content.innerHTML, /Runtime/);
  assert.match(content.innerHTML, /UI health/);
  assert.match(content.innerHTML, /Selected Book/);
  assert.match(content.innerHTML, /Source files/);
  assert.match(content.innerHTML, /Book Alpha/);
  assert.match(content.innerHTML, /1 source folder unavailable/);
  assert.doesNotMatch(content.innerHTML, /Background workers/);
  assert.doesNotMatch(content.innerHTML, /UI responsiveness/);
  assert.doesNotMatch(content.innerHTML, /data-action="diagnostic-book"/);
});

test("Diagnostics Tasks retains only twenty worker rows", () => {
  const { messageHandler, content, contentListeners } = loadBridge("diagnostics");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });
  messageHandler({ data: { version: 1, id: "tasks", ok: true, command: "background.tasks", payload: Array.from({ length: 25 }, (_, index) => ({ kind: `Worker ${index + 1}`, state: index === 0 ? "Running" : "Completed", subject: "Book Alpha", step: "Processing" })) } });
  const target = { dataset: { action: "diagnostics-tab", diagnosticsTab: "tasks" } };
  target.closest = () => target;
  contentListeners.click({ target });

  assert.match(content.innerHTML, /data-diagnostics-panel="tasks"/);
  assert.match(content.innerHTML, /Background workers/);
  assert.match(content.innerHTML, /Active/);
  assert.match(content.innerHTML, /Retained/);
  assert.match(content.innerHTML, /Worker 20/);
  assert.doesNotMatch(content.innerHTML, /Worker 21/);
  assert.doesNotMatch(content.innerHTML, /UI responsiveness/);
  assert.doesNotMatch(content.innerHTML, /Recent logs/);
});

test("Diagnostics Performance excludes zero-duration lifecycle noise", () => {
  const { messageHandler, content, contentListeners } = loadBridge("diagnostics");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });
  messageHandler({ data: { version: 1, id: "diagnostics", ok: true, command: "diagnostics.snapshot", payload: [
    { timestamp: "2026-08-26T11:21:00Z", severity: 0, operation: "task.queued", durationMilliseconds: 0 },
    { timestamp: "2026-08-26T11:21:10Z", severity: 0, operation: "task.started", durationMilliseconds: 0 },
    { timestamp: "2026-08-26T11:21:20Z", severity: 2, operation: "snapshot.refresh", durationMilliseconds: 4510 },
    { timestamp: "2026-08-26T11:21:30Z", severity: 2, operation: "worker.LibraryRefresh", durationMilliseconds: 4576 },
    { timestamp: "2026-08-26T11:21:40Z", severity: 0, operation: "task.completed", durationMilliseconds: 0 }
  ] } });
  const target = { dataset: { action: "diagnostics-tab", diagnosticsTab: "performance" } };
  target.closest = () => target;
  contentListeners.click({ target });

  assert.match(content.innerHTML, /Slow operations[\s\S]*?>2</);
  assert.match(content.innerHTML, /Worst duration[\s\S]*?>4576 ms</);
  assert.match(content.innerHTML, /Latest slow operation[\s\S]*?>worker\.LibraryRefresh</);
  assert.match(content.innerHTML, /snapshot\.refresh/);
  assert.match(content.innerHTML, /worker\.LibraryRefresh/);
  assert.doesNotMatch(content.innerHTML, /task\.queued/);
  assert.doesNotMatch(content.innerHTML, /task\.started/);
  assert.doesNotMatch(content.innerHTML, /task\.completed/);
  assert.doesNotMatch(content.innerHTML, /Background workers/);
});

test("Diagnostics Performance shows an empty duration when there are no slow operations", () => {
  const { messageHandler, content, contentListeners } = loadBridge("diagnostics");
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });
  const target = { dataset: { action: "diagnostics-tab", diagnosticsTab: "performance" } };
  target.closest = () => target;
  contentListeners.click({ target });

  assert.match(content.innerHTML, /Worst duration[\s\S]*?>—</);
  assert.match(content.innerHTML, /No meaningful UI performance operations recorded/);
});

test("Diagnostics Book keeps a selected Book and only renders twelve meaningful logs", () => {
  const { messageHandler, content, contentListeners } = loadBridge("diagnostics");
  const snapshot = diagnosticsSnapshot();
  snapshot.bookSummaries[0].logs = [
    { eventName: "", detail: "" },
    { eventName: "", detail: "." },
    ...Array.from({ length: 15 }, (_, index) => ({ timestamp: `2026-08-26T11:${String(index).padStart(2, "0")}:00Z`, eventName: `Log ${index + 1}`, detail: "Saved" }))
  ];
  snapshot.discovery.books.push({ id: { value: "Book Beta" }, name: "Book Beta" });
  snapshot.bookSummaries.push({ bookId: { value: "Book Beta" }, workspaceStatus: "Completed", sourceFolders: [], logs: [] });
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: snapshot } });
  const tab = { dataset: { action: "diagnostics-tab", diagnosticsTab: "book" } };
  tab.closest = () => tab;
  contentListeners.click({ target: tab });

  assert.match(content.innerHTML, /data-action="diagnostic-book"/);
  assert.match(content.innerHTML, /Workspace/);
  assert.match(content.innerHTML, /Source folders/);
  assert.match(content.innerHTML, /Recent logs/);
  assert.match(content.innerHTML, /Log 15/);
  assert.match(content.innerHTML, /Log 4/);
  assert.doesNotMatch(content.innerHTML, /Log 3/);
  assert.doesNotMatch(content.innerHTML, />\.<\/span>/);
  assert.equal((content.innerHTML.match(/Log \d+/g) ?? []).length, 12);

  contentListeners.change({ target: { dataset: { action: "diagnostic-book" }, value: "Book Beta" } });
  assert.match(content.innerHTML, /Book Beta/);
  assert.doesNotMatch(content.innerHTML, /Log 15/);
});

test("Diagnostics refresh uses the existing bridge protocol from every tab and preserves local context", () => {
  for (const tabName of ["summary", "tasks", "performance", "book"]) {
    const { messageHandler, content, contentListeners, messages } = loadBridge("diagnostics");
    messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: diagnosticsSnapshot() } });
    if (tabName !== "summary") {
      const tab = { dataset: { action: "diagnostics-tab", diagnosticsTab: tabName } };
      tab.closest = () => tab;
      contentListeners.click({ target: tab });
    }
    const refresh = { dataset: { action: "refresh-diagnostics" } };
    refresh.closest = () => refresh;
    const messageCount = messages.length;
    contentListeners.click({ target: refresh });
    assert.deepEqual(messages.slice(messageCount).map((message) => message.command), ["diagnostics.get", "task.list"]);
    messageHandler({ data: { version: 1, id: "diagnostics", ok: true, command: "diagnostics.snapshot", payload: [] } });
    messageHandler({ data: { version: 1, id: "tasks", ok: true, command: "background.tasks", payload: [] } });
    assert.match(content.innerHTML, new RegExp(`data-diagnostics-panel="${tabName}"`));
  }

  const { messageHandler, content, contentListeners } = loadBridge("diagnostics");
  const snapshot = diagnosticsSnapshot();
  snapshot.discovery.books.push({ id: { value: "Book Beta" }, name: "Book Beta" });
  snapshot.bookSummaries.push({ bookId: { value: "Book Beta" }, workspaceStatus: "Completed", sourceFolders: [], logs: [] });
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: snapshot } });
  const bookTab = { dataset: { action: "diagnostics-tab", diagnosticsTab: "book" } };
  bookTab.closest = () => bookTab;
  contentListeners.click({ target: bookTab });
  contentListeners.change({ target: { dataset: { action: "diagnostic-book" }, value: "Book Beta" } });
  messageHandler({ data: { version: 1, id: "diagnostics", ok: true, command: "diagnostics.snapshot", payload: [] } });
  assert.match(content.innerHTML, /data-diagnostics-panel="book"/);
  assert.match(content.innerHTML, /value="Book Beta" selected/);
});

test("Diagnostics route requests and renders sanitized responsiveness events", () => {
  const { messageHandler, content, routeButtons, messages } = loadBridge("diagnostics");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [] }, globalSettings: {}, bookSummaries: []
  } } });
  routeButtons.find((button) => button.dataset.route === "diagnostics").listeners.click();
  assert.deepEqual(messages.slice(-2).map((message) => message.command), ["diagnostics.get", "task.list"]);
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "diagnostics.snapshot", payload: [{ timestamp: "2026-08-25T00:00:00Z", severity: "Slow", kind: "dispatcher.stall", operation: "dispatcher", durationMilliseconds: 300, subject: null, activeOperations: ["book.scan (Book 001)"] }] } });
  assert.equal(content.innerHTML.includes("UI responsiveness"), false);
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.tasks", payload: [{ kind: "LibraryRefresh", state: "Running", subject: "Library", step: "discovery" }] } });
  assert.match(content.innerHTML, /Active/);
});
