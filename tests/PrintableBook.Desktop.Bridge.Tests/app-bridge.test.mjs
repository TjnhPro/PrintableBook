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

test("snapshot rendering keeps discovery, settings, and brand data in the bridge response", () => {
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
  assert.match(content.innerHTML, /Maximum concurrency/);
  assert.match(content.innerHTML, /value="6"/);
  assert.doesNotMatch(content.innerHTML, /Paths \(Read Only\)/);
});

test("phase 4 page markup includes the interior-only processing workflow", () => {
  const script = readFileSync(appScriptPath, "utf8");

  for (const state of ["Selected queue", "Process Interior", "Published outputs", "Workspace logs", "Settings saved", "Brand settings", "Interior processing", "Current step"]) {
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
