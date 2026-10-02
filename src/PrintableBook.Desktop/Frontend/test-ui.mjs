import { readFileSync } from "node:fs";

const app = readFileSync(new URL("./js/app.js", import.meta.url), "utf8");
const expected = [
  'tabButton("settings", "Settings")',
  "No processed pages",
  "book.interior.settings.save",
  "Save changes",
  "Use Brand background",
  "Intro pages",
  "intro-template-page",
  "Process Interior",
  "Diagnostics",
  "Interrupted",
  "Stopping processing…",
  "Interior pages prepared. Existing PDF unchanged.",
  "Saving previews",
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
  ,"process-workspace"
  ,"Selected queue"
  ,"process-queue-grid-scroll"
  ,"process-queue-pagination"
  ,"remove-process-queue-book"
  ,"Run needs review"
  ,"Elapsed"
  ,"Preview"
  ,"Open Folder"
  ,"book.output.open"
  ,"book.output.copy-path"
  ,"Brands & templates"
  ,"Search Brands"
  ,"Fix these Brand assets"
  ,"Required image size"
  ,"Current size"
  ,"Production Assets"
  ,"Build Cover PDF"
  ,"Process Interior Cover"
  ,"Process Book Owner"
  ,"Build Final Interior"
  ,"book.production.asset.import"
  ,"book.production.action.start"
  ,"production-interior"
  ,'role="${state.catalogFeedbackError ? "alert" : "status"}">'
  ,'role="tablist"'
  ,'role="tabpanel"'
  ,'aria-controls="book-panel-'
];

for (const value of expected) {
  if (!app.includes(value)) throw new Error(`Missing UI contract: ${value}`);
}

if (app.includes("Advanced JSON settings") || app.includes("brand.settings")) {
  throw new Error("Removed Brand settings UI must not remain in the bridge contract.");
}

if (app.includes('tabButton("settings", "Interior settings")') || app.includes('tabButton("pages", "Interior pages")')) {
  throw new Error("Hidden Book detail tabs must not remain in the visible tablist.");
}

if (app.includes('tabButton("overview"') || app.includes("Review the summary and Brand background before processing")) {
  throw new Error("The legacy Book Overview summary must not remain in the visible Book detail UI.");
}

console.log(`UI contract passed (${expected.length} checks).`);
