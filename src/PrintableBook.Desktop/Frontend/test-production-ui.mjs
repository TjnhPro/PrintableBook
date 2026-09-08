import { readFileSync } from "node:fs";

const root = new URL("../../../", import.meta.url);
const read = (relativePath) => readFileSync(new URL(relativePath, root), "utf8");
const html = read("src/PrintableBook.Desktop/Frontend/index.html");
const app = read("src/PrintableBook.Desktop/Frontend/js/app.js");
const css = read("src/PrintableBook.Desktop/Frontend/css/input.css");
const workspaceCss = read("src/PrintableBook.Desktop/Frontend/css/book-workspace.css");
const window = read("src/PrintableBook.Desktop/MainWindow.xaml");
const windowCode = read("src/PrintableBook.Desktop/MainWindow.xaml.cs");

for (const [source, value] of [[html, 'id="global-process-status"'], [html, 'id="refresh-button"'], [html, 'id="update-check-button"'], [html, 'Check updates'], [html, 'id="update-banner"'], [html, 'aria-live="polite"'], [html, 'Version 0.1'], [html, 'assets/printable-book-logo.png'], [app, "aria-live=\"polite\""], [app, "role=\"alert\""], [app, "updateGlobalRefreshControl"], [app, "renderUpdateBanner"], [app, 'escapeHtml(valueFor(snapshot, "releaseNotes"'], [app, "Refreshing…"], [css, "prefers-reduced-motion"], [css, "aspect-square"], [css, '[data-theme="dark"]'], [workspaceCss, ".update-banner"], [workspaceCss, ".update-progress"], [workspaceCss, ".update-release-notes"], [window, 'Icon="Assets/app-icon.ico"'], [window, 'MinHeight="950"'], [window, 'MinWidth="1650"'], [windowCode, 'PreferredWindowSize = new(1650, 950)'], [windowCode, 'ConstrainToWorkingArea']]) {
  if (!source.includes(value)) throw new Error(`Production UI certification failed: ${value}`);
}

if (html.includes("../Assets/app-icon-source.png")) {
  throw new Error(
    "Production UI certification failed: external Assets icon reference remains.");
}

if (app.includes("innerHTML = releaseNotes")) {
  throw new Error("Production UI certification failed: release notes are inserted without escaping.");
}

console.log("Production UI certification passed (26 checks).");
