import { readFileSync } from "node:fs";

const app = readFileSync(new URL("./js/app.js", import.meta.url), "utf8");
const expected = [
  "Overview",
  "Validation",
  "Processing",
  "Outputs",
  "Logs",
  "book.cover.select",
  "book.interior.frame-mode.set",
  "Frame mode",
  "Interior frame mode",
  "No Frame",
  "Process Interior",
  "Diagnostics",
  "Interrupted",
  "Stopping processing…",
  "Last Interior Processing session",
  "Last session",
  "Start New Interior Processing"
  ,"Book Library"
  ,"Nothing processing"
  ,"global-process-status"
  ,"book.asset.preview.get"
  ,"assetPreviews"
];

for (const value of expected) {
  if (!app.includes(value)) throw new Error(`Missing UI contract: ${value}`);
}

console.log(`UI contract passed (${expected.length} checks).`);
