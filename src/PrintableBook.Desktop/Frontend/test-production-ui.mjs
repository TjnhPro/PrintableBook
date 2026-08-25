import { readFileSync } from "node:fs";

const root = new URL("../../../", import.meta.url);
const read = (relativePath) => readFileSync(new URL(relativePath, root), "utf8");
const html = read("src/PrintableBook.Desktop/Frontend/index.html");
const app = read("src/PrintableBook.Desktop/Frontend/js/app.js");
const css = read("src/PrintableBook.Desktop/Frontend/css/input.css");
const window = read("src/PrintableBook.Desktop/MainWindow.xaml");

for (const [source, value] of [[html, 'id="global-process-status"'], [app, "aria-live=\"polite\""], [app, "role=\"alert\""], [css, "prefers-reduced-motion"], [css, "aspect-square"], [css, '[data-theme="dark"]'], [window, 'MinWidth="1650"'], [window, 'MinHeight="950"']]) {
  if (!source.includes(value)) throw new Error(`Production UI certification failed: ${value}`);
}

console.log("Production UI certification passed (8 checks).");
