import { readFileSync } from "node:fs";

const app = readFileSync(new URL("./js/app.js", import.meta.url), "utf8");
const expected = [
  "Overview",
  "Interior settings",
  "book.interior.settings.save",
  "Save changes",
  "Use Brand background",
  "Intro pages",
  "intro-template-page",
  "Process Interior",
  "Diagnostics",
  "Interrupted",
  "Stopping processing…",
  "Last Interior Processing session",
  "Last session",
  "Start New Interior Processing"
  ,"Nothing processing"
  ,"global-process-status"
  ,"localImageUrl"
  ,"data-local-image"
  ,"width=\"256\" height=\"256\""
  ,"decoding=\"async\""
  ,"data-intro-total-pages"
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
