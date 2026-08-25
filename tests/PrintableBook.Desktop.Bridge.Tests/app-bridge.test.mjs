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

function loadBridge(activeRoute = null) {
  const status = { textContent: "" };
  const contentListeners = {};
  const content = { innerHTML: "", addEventListener: (eventName, handler) => { contentListeners[eventName] = handler; } };
  const brandSelect = { innerHTML: "", value: "", addEventListener: () => { } };
  const refreshButton = { addEventListener: () => { } };
  const messages = [];
  const intervals = [];
  let messageHandler;
  const browserWindow = {
    chrome: {
      webview: {
        addEventListener: (_eventName, handler) => { messageHandler = handler; },
        postMessage: (message) => { messages.push(JSON.parse(message)); }
      }
    },
    setInterval: (callback) => { intervals.push(callback); return intervals.length; }
  };

  vm.runInNewContext(readFileSync(appScriptPath, "utf8"), {
    crypto: { randomUUID: () => "request-1" },
    document: {
      getElementById: (id) => ({ "bridge-status": status, "app-content": content, "brand-select": brandSelect, "refresh-button": refreshButton }[id]),
      querySelectorAll: () => [],
      querySelector: (selector) => selector === ".nav-item-active" && activeRoute ? { dataset: { route: activeRoute } } : null
    },
    window: browserWindow
  });

  return { messageHandler, status, content, brandSelect, contentListeners, intervals, messages };
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
  assert.match(content.innerHTML, /Books \(1\)/);
  assert.match(content.innerHTML, /Book 001/);
  assert.match(content.innerHTML, /Process Interior/);
  assert.doesNotMatch(content.innerHTML, /Paths \(Read Only\)/);
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

test("active processing is polled globally and stops after a terminal snapshot", () => {
  const { messageHandler, content, intervals, messages } = loadBridge("books");

  assert.equal(intervals.length, 1);
  messageHandler({ data: { version: 1, id: "process-1", ok: true, command: "process.snapshot", payload: { isActive: true, isCancelling: false } } });
  assert.equal(content.innerHTML, "");
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

test("book filters render the recovered interrupted workspace status", () => {
  const { messageHandler, content } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-1", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, workspaceStatus: 5, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [] }]
  } } });

  assert.match(content.innerHTML, /Interrupted/);
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
      interiorSourcePages: [{ sourceReference: "Book interior/page-001.png", frameMode }]
    }]
  });

  messageHandler({ data: { version: 1, id: "book-1", ok: true, command: "app.snapshot", payload: snapshot(0) } });

  assert.match(content.innerHTML, /Interior frame mode/);
  assert.match(content.innerHTML, /Auto uses detected artwork type/);
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

test("asset workspace loads an allowlisted preview only after the user selects an asset", () => {
  const { messageHandler, content, contentListeners, messages } = loadBridge("books");
  messageHandler({ data: { version: 1, id: "book-asset", ok: true, command: "app.snapshot", payload: {
    discovery: { brands: [], books: [{ id: { value: "Book 001" }, name: "Book 001" }] },
    globalSettings: {},
    bookSummaries: [{ bookId: { value: "Book 001" }, validationChecks: [], sourceFolders: [], publishedArtifacts: [], interiorPages: [], logs: [], assets: [{ sourceReference: "Book interior/page-001.png", relativePath: "Book interior/page-001.png", fileName: "page-001.png", folder: "Book interior", kind: "Interior", width: 2550, height: 2550, frameMode: "auto", previewAvailable: true }] }]
  } } });

  const assetsTab = { dataset: { action: "book-tab", bookTab: "assets" }, closest: () => assetsTab };
  contentListeners.click({ target: assetsTab });
  assert.match(content.innerHTML, /Asset Workspace/);
  assert.match(content.innerHTML, /page-001\.png/);
  assert.match(content.innerHTML, /Load preview/);

  const asset = { dataset: { action: "select-asset", sourceReference: "Book interior/page-001.png" }, closest: () => asset };
  contentListeners.click({ target: asset });
  assert.deepEqual(messages.at(-1), {
    version: 1,
    id: "request-1",
    command: "book.asset.preview.get",
    payload: { bookId: "Book 001", sourceReference: "Book interior/page-001.png" }
  });
});

test("validation keeps missing cover informational for Interior and actionable for full-book review", () => {
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

  const validationTab = { dataset: { action: "book-tab", bookTab: "validation" }, closest: () => validationTab };
  contentListeners.click({ target: validationTab });
  assert.match(content.innerHTML, /Cover is optional for this Interior-only run/);
  assert.match(content.innerHTML, /Interior Processing can continue without a Cover/);
  assert.match(content.innerHTML, /Informational/);

  const fullBook = { dataset: { action: "validation-mode", validationMode: "full-book" }, closest: () => fullBook };
  contentListeners.click({ target: fullBook });
  assert.match(content.innerHTML, /A Cover PNG is required/);
  assert.match(content.innerHTML, /Refresh local files/);
  assert.match(content.innerHTML, /Needs attention/);
});
