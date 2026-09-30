import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const temporary = mkdtempSync(join(tmpdir(), "printablebook-css-"));
const generated = join(temporary, "tailwind.css");

try {
  const cli = join(root, "node_modules", "tailwindcss", "lib", "cli.js");
  const result = spawnSync(process.execPath, [cli, "-i", join(root, "css", "input.css"), "-o", generated, "--minify"], {
    cwd: root,
    encoding: "utf8"
  });
  if (result.status !== 0) throw new Error(result.stderr || result.stdout || "Tailwind CSS generation failed.");
  const expected = readFileSync(join(root, "css", "tailwind.css"));
  const actual = readFileSync(generated);
  if (!expected.equals(actual)) throw new Error("css/tailwind.css is stale. Run npm run build:css and commit the result.");
  console.log("Committed Tailwind CSS is current.");
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
