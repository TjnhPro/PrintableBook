import { readFileSync } from "node:fs";

const root = new URL("../../../", import.meta.url);
const read = (relativePath) => readFileSync(new URL(relativePath, root), "utf8");
const html = read("src/PrintableBook.Desktop/Frontend/index.html");
const app = read("src/PrintableBook.Desktop/Frontend/js/app.js");
const css = read("src/PrintableBook.Desktop/Frontend/css/input.css");
const window = read("src/PrintableBook.Desktop/MainWindow.xaml");
const windowCode = read("src/PrintableBook.Desktop/MainWindow.xaml.cs");

for (const [source, value] of [[html, 'id="global-process-status"'], [html, 'id="refresh-button"'], [app, "aria-live=\"polite\""], [app, "role=\"alert\""], [app, "updateGlobalRefreshControl"], [app, "Refreshing…"], [css, "prefers-reduced-motion"], [css, "aspect-square"], [css, '[data-theme="dark"]'], [window, 'SourceInitialized="OnSourceInitialized"'], [windowCode, 'PreferredWindowSize = new(1650, 950)'], [windowCode, 'ConstrainToWorkingArea']]) {
  if (!source.includes(value)) throw new Error(`Production UI certification failed: ${value}`);
}

console.log("Production UI certification passed (11 checks).");
