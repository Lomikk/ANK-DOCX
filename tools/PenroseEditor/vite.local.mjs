// Run the unmodified official editor against published Penrose packages rather
// than monorepo source aliases. Development mode exposes its built-in Roger tab.
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react-swc";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";

// MathJax's browser build requires its version constant; otherwise it falls
// back to eval('require') from its Node entry and fails before React mounts.
const mathjaxVersion = JSON.stringify(JSON.parse(readFileSync(new URL("./node_modules/mathjax-full/package.json", import.meta.url), "utf8")).version);

export default defineConfig({
  define: { PACKAGE_VERSION: mathjaxVersion },
  base: "/try/",
  plugins: [react({ jsxRuntime: "classic" }), {
    name: "local-katex-css",
    transformIndexHtml: html => html.replace(
      /https:\/\/cdnjs\.cloudflare\.com\/ajax\/libs\/KaTeX\/[^\"]+\/katex\.min\.css/,
      "/try/node_modules/katex/dist/katex.min.css"),
  }],
  worker: { format: "es" },
  build: { target: "esnext" },
  optimizeDeps: { esbuildOptions: { target: "esnext", define: { PACKAGE_VERSION: mathjaxVersion } }, exclude: ["@penrose/examples", "rose"] },
  // The upstream gallery is a private monorepo package. Local projects use Roger's
  // list instead; no online gallery or previews are fetched for this installation.
  resolve: { alias: {
    "@penrose/examples/dist/registry.js": fileURLToPath(new URL("./examples.local.mjs", import.meta.url)),
    "svgo": fileURLToPath(new URL("./node_modules/svgo/dist/svgo.browser.js", import.meta.url)),
  } },
  server: {
    host: "127.0.0.1", port: 3000, strictPort: true,
    proxy: process.env.ANK_SAVE_URL ? {
      "/ank/": { target: process.env.ANK_SAVE_URL, headers: { "X-Ank-Session": process.env.ANK_SAVE_TOKEN } },
    } : {},
    headers: { "Cross-Origin-Opener-Policy": "same-origin-allow-popups" },
  },
});
