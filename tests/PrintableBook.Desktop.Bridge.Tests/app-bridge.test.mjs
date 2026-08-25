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
    requestAnimationFrame: (callback) => { callback(); return 1; }
  };

  vm.runInNewContext(readFileSync(appScriptPath, "utf8"), {
    crypto: { randomUUID: () => "request-1" },
    document: {
      getElementById: (id) => ({ "bridge-status": status, "app-content": content, "brand-select": brandSelect, "refresh-button": refreshButton }[id]),
      querySelectorAll: (selector) => selector === "[data-preview-book-id][data-source-reference]" ? visibleTiles : selector === "[data-route]" ? routeButtons : [],
      querySelector: (selector) => selector === "[data-brand-settings]" ? brandSettingsEditor : selector === ".nav-item-active" && activeRoute ? { dataset: { route: activeRoute } } : null,
      addEventListener: (eventName, handler) => { documentListeners[eventName] = handler; }
    },
    window: browserWindow,
    CSS: { escape: (value) => String(value).replace(/["\\]/g, "\\$&") }
  });

  return { messageHandler, status, content, brandSelect, brandSettingsEditor, refreshButton, contentListeners, documentListeners, routeButtons, intervals, messages };
}

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
      assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", width: 2550, height: 2550, frameMode }]
    }]
  });

  messageHandler({ data: { version: 1, id: "book-1", ok: true, command: "app.snapshot", payload: snapshot(0) } });

  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: assetsTab });

  assert.match(content.innerHTML, /Interior assets/);
  assert.match(content.innerHTML, /Choose its frame mode directly on the image/);
  assert.match(content.innerHTML, /option value="auto" selected/);
  contentListeners.change({ target: { dataset: { action: "set-interior-frame-mode", bookId: "Book 001", sourceReference: "Book interior/page-001.png" }, value: "enabled" } });
  assert.deepEqual(messages.at(-1), {
    version: 1,
    id: "request-1",
    command: "book.interior.frame-mode.set",
    payload: { bookId: "Book 001", sourceReference: "Book interior/page-001.png", mode: "enabled" }
  });

  messageHandler({ data: { version: 1, id: "book-2", ok: true, command: "app.snapshot", payload: snapshot(1) } });
  assert.match(content.innerHTML, /option value="enabled" selected/);
});

test("asset workspace queues allowlisted previews for visible Interior assets", () => {
  const visibleTile = {
    dataset: { previewBookId: "Book 001", sourceReference: "Book interior/page-001.png" },
    getBoundingClientRect: () => ({ top: 0, left: 0, right: 1, bottom: 1 })
  };
  const { messageHandler, content, contentListeners, messages } = loadBridge("books", [visibleTile]);
  messageHandler({ data: { version: 1, id: "book-asset", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [], assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", width: 2550, height: 2550, frameMode: "auto", previewAvailable: true }] }]
  } } });

  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  contentListeners.click({ target: openBook });
  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: assetsTab });
  assert.match(content.innerHTML, /Interior assets/);
  assert.match(content.innerHTML, /page-001\.png/);
  assert.match(content.innerHTML, /Loading preview/);
  assert.deepEqual(messages.at(-1), {
    version: 1,
    id: "request-1",
    command: "book.asset.preview.get",
    payload: { bookId: "Book 001", sourceReference: "Book interior/page-001.png" }
  });
});

test("asset preview worker completion fetches its result exactly once", () => {
  const visibleTile = { dataset: { previewBookId: "Book 001", sourceReference: "Book interior/page-001.png" }, getBoundingClientRect: () => ({ top: 0, left: 0, right: 1, bottom: 1 }) };
  const { messageHandler, contentListeners, intervals, messages } = loadBridge("books", [visibleTile]);
  messageHandler({ data: { version: 1, id: "snapshot", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] }, globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", previewAvailable: true }], validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [] }]
  } } });
  const openBook = { dataset: { action: "select-book", bookId: "Book 001" }, closest: () => openBook };
  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: openBook });
  contentListeners.click({ target: assetsTab });

  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.task", payload: { taskId: "preview-1", kind: "AssetPreview", state: "Running" } } });
  intervals.at(-1)();
  assert.equal(messages.at(-1).command, "task.get");
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.task", payload: { taskId: "preview-1", kind: "AssetPreview", state: "Completed" } } });
  assert.equal(messages.at(-1).command, "book.asset.preview.result");
  const resultCount = messages.filter((message) => message.command === "book.asset.preview.result").length;
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.task", payload: { taskId: "preview-1", kind: "AssetPreview", state: "Completed" } } });
  assert.equal(messages.filter((message) => message.command === "book.asset.preview.result").length, resultCount);
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

test("output review shows verified PDF facts and only sends a book-scoped action", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("outputs");
  messageHandler({ data: { version: 1, id: "output-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [] }, globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, outputSummaries: [{ artifactReference: "D:/PrintableBook/outputs/Book-001-interior.pdf", fileName: "Book-001-interior.pdf", fileSizeBytes: 2200000, pageCount: 42, widthInches: 8.5, heightInches: 8.5, verificationStatus: "Verified", generatedAt: "2026-08-25T00:00:00Z" }] }]
  } } });
  assert.match(content.innerHTML, /Book-001-interior\.pdf/);
  assert.match(content.innerHTML, /42/);
  assert.match(content.innerHTML, /2\.1 MB/);
  assert.match(content.innerHTML, /Reveal in Explorer/);
  const open = { dataset: { action: "open-output", bookId: "Book 001", artifactReference: "D:/PrintableBook/outputs/Book-001-interior.pdf" }, closest: () => open };
  contentListeners.click({ target: open });
  assert.deepEqual(messages.at(-1), { version: 1, id: "request-1", command: "book.output.open", payload: { bookId: "Book 001", artifactReference: "D:/PrintableBook/outputs/Book-001-interior.pdf" } });
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
  messageHandler({ data: { version: 1, id: "request-1", ok: true, command: "background.tasks", payload: [{ kind: "AssetPreview", state: "Running", subject: "Book 001/page.png", step: "preview.generate" }] } });
  assert.match(content.innerHTML, /Background workers/);
  assert.match(content.innerHTML, /AssetPreview/);
});
