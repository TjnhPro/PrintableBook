import { readFileSync } from "node:fs";

const app = readFileSync(new URL("./js/app.js", import.meta.url), "utf8");
const expected = [
  'tabButton("settings", "Settings")',
  'tabButton("asin", "Keyword")',
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
  ,"production-group-grid"
  ,"Source image"
  ,"Preview image"
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
  ,"S3 Storage"
  ,"s3.credentials.replace"
  ,"book.s3.check"
  ,"book.s3.upload"
  ,"data-book-s3"
  ,'asset-background-setting book-settings-card book-settings-s3 storage-book-card'
  ,"Replace credentials"
  ,"seven-file publication package"
  ,"storage-open-url"
  ,"storage-copy-url"
  ,"storage-file-facts"
  ,"storage-active-owner"
  ,"missingArtifacts"
  ,"uploadCompletedCount"
  ,'aria-live="polite"'
  ,'supportedLanguages'
  ,'brandCloneDestinationName'
  ,'data-action="open-brand-clone"'
  ,'data-action="clone-brand-language"'
  ,'data-action="submit-brand-clone"'
  ,'command === "brand.clone.completed"'
  ,'Brand cloned. Validate this Brand before processing.'
  ,'Cloning…'
  ,'invalid_brand_clone'
  ,'brand_clone_destination_exists'
  ,'brand_clone_source_not_found'
  ,'brand_clone_language_invalid'
  ,'snapshot_unavailable'
  ,'brand_clone_failed'
  ,'state.brandCloneAwaitingSnapshot = true'
];

for (const value of expected) {
  if (!app.includes(value)) throw new Error(`Missing UI contract: ${value}`);
}

if (app.includes("Advanced JSON settings") || app.includes("brand.settings")) {
  throw new Error("Removed Brand settings UI must not remain in the bridge contract.");
}

if (app.includes('tabButton("settings", "Interior settings")') || app.includes('tabButton("asin", "ASIN Research")') || app.includes('tabButton("pages", "Interior pages")')) {
  throw new Error("Hidden Book detail tabs must not remain in the visible tablist.");
}

if (app.includes('tabButton("overview"') || app.includes("Review the summary and Brand background before processing")) {
  throw new Error("The legacy Book Overview summary must not remain in the visible Book detail UI.");
}

if (app.includes("storage.settings.save") || app.includes("storage.book.check") || app.includes("storage.book.upload")) {
  throw new Error("Legacy standalone Storage bridge commands must not remain in the UI.");
}

if (!/const cloneBlocked = !state\.brandCloneLanguageCode \|\| !cloneDestination \|\| cloneDestinationExists \|\| cloneBusy \|\| processIsActive\(\)/.test(app)) {
  throw new Error("Clone Brand must stay disabled until the request is safe to submit.");
}

if (!/state\.brandFilter = "";[\s\S]*state\.inspectedBrand = cloneDestination;[\s\S]*resetBrandClone\(false\)/.test(app)) {
  throw new Error("Clone refresh must reveal and select the newly cloned Brand.");
}

console.log(`UI contract passed (${expected.length} checks).`);
