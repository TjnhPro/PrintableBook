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
  let messageHandler;

  vm.runInNewContext(readFileSync(appScriptPath, "utf8"), {
    crypto: { randomUUID: () => "request-1" },
    document: { getElementById: () => status },
    window: {
      chrome: {
        webview: {
          addEventListener: (_eventName, handler) => { messageHandler = handler; },
          postMessage: () => { }
        }
      }
    }
  });

  return { messageHandler, status };
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

  assert.equal(status.textContent, "Desktop bridge connected.");
});
