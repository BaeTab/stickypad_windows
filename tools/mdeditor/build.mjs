import { build } from "esbuild";

await build({
  entryPoints: ["editor.js"],
  bundle: true,
  format: "iife",
  minify: true,
  sourcemap: false,
  target: ["chrome110"],
  legalComments: "none",
  outfile: "dist/mdeditor.bundle.js",
});

console.log("✓ built dist/mdeditor.bundle.js");
