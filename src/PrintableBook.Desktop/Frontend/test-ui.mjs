import { readFileSync } from "node:fs";

const app = readFileSync(new URL("./js/app.js", import.meta.url), "utf8");
const expected = [
  "Overview",
  "Validation",
  "Processing",
  "Outputs",
  "Logs",
  "book.cover.select",
  "Cover selection",
  "Process selected",
  "Diagnostics"
];

for (const value of expected) {
  if (!app.includes(value)) throw new Error(`Missing UI contract: ${value}`);
}

console.log(`UI contract passed (${expected.length} checks).`);
