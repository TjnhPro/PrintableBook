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

function loadBridge() {
  const status = { textContent: "" };
  const content = { innerHTML: "", addEventListener: () => { } };
  const brandSelect = { innerHTML: "", value: "" };
  const refreshButton = { addEventListener: () => { } };
  const messages = [];
  let messageHandler;
  const browserWindow = {
    chrome: {
      webview: {
        addEventListener: (_eventName, handler) => { messageHandler = handler; },
        postMessage: (message) => { messages.push(JSON.parse(message)); }
      }
    }
  };

  vm.runInNewContext(readFileSync(appScriptPath, "utf8"), {
    crypto: { randomUUID: () => "request-1" },
    document: {
      getElementById: (id) => ({ "bridge-status": status, "app-content": content, "brand-select": brandSelect, "refresh-button": refreshButton }[id]),
      querySelectorAll: () => [],
      querySelector: () => null
    },
    window: browserWindow
  });

  return { messageHandler, status, content, brandSelect, messages };
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
  assert.match(content.innerHTML, /Maximum page concurrency/);
  assert.match(content.innerHTML, /value="6"/);
  assert.match(content.innerHTML, /D:\/PrintableBook/);
});

test("phase 4 page markup includes durable process, output, diagnostics, and bridge workflow states", () => {
  const script = readFileSync(appScriptPath, "utf8");

  for (const state of ["Live session", "Selected queue", "Published outputs", "Application diagnostics", "Settings saved", "Brand settings", "Processing history", "Interior page detail"]) {
    assert.match(script, new RegExp(state));
  }
  assert.match(script, /command: "settings\.save"/);
  assert.match(script, /command: "brand\.settings\.save"/);
  assert.match(script, /command: "book\.validate"/);
  assert.match(script, /command: "process\.start"/);
  assert.match(script, /command: "process\.cancel"/);
  assert.match(script, /command: "app\.refresh"/);
});
