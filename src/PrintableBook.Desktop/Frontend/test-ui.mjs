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
  ,"Asset Workspace"
  ,"Load preview"
  ,"filter-assets"
  ,"asset-view"
  ,"Interior preflight"
  ,"Full-book preflight"
  ,"Ready to process"
  ,"validation-mode"
  ,"Current stage"
  ,"process-status-strip"
  ,"Run needs review"
  ,"Elapsed"
  ,"Open PDF"
  ,"Reveal in Explorer"
  ,"book.output.open"
  ,"book.output.copy-path"
  ,"Brands & templates"
  ,"Advanced JSON settings"
];

for (const value of expected) {
  if (!app.includes(value)) throw new Error(`Missing UI contract: ${value}`);
}

console.log(`UI contract passed (${expected.length} checks).`);
