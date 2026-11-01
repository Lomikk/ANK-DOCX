# Local official Penrose editor

This folder contains the local deployment configuration and an npm lock, not a
second diagram editor. `source.json` pins the official MIT-licensed Penrose 3.3.1
source archive by commit and SHA-256. The installer extracts only `packages/editor`
and its license into `.tools/penrose-editor/`, then runs npm ci there with local Node.
Installation is lazy on first opening, or explicit through `figures NAME setup`.
No global Node, npm or browser settings are changed.

The editor runs in Vite development mode to expose the upstream Roger panel.
The root monorepo aliases are replaced by the official published core/components
3.3.1 packages. Configuration differences from the monorepo:

- `tsconfig.json` is self-contained instead of extending the monorepo root.
- SVGO uses its published browser entry point.
- KaTeX CSS and fonts come from the local npm package, replacing the CDN stylesheet.
- MathJax's `PACKAGE_VERSION` build constant is set from its installed package.json
  for both Vite and dependency optimization. Without it the Node fallback executes
  `eval('require')` in the browser and leaves a blank page before React mounts.
- The private examples registry is an empty Map; local projects are selected through
  the upstream Roger trio list. The online gallery is not part of this installation.
- Modern browsers' native top-level await is used (`target: esnext`); the older
  top-level-await transform failed with the current SWC and is not needed here.

Two narrowly scoped upstream gaps require adaptation, both checked against pinned
source text rather than a permissive patch:

1. `RogerPanel.tsx`'s legacy individual `trio_file` listener used an AND guard. It
   also accepted the aggregate `trio_files` response and supplied undefined file
   names to the selection handler. It uses OR in the installed editor; App already
   handles the aggregate response.
2. Roger 3.3.1 offers a port but no host option. The session makes a sibling
   `watch-local.js` copy changing the WebSocket listener configuration: bind to
   `127.0.0.1` and accept only the local editor's Origin. It retains
   official discovery and request handling. The original installed module is intact.

Vite binds only `127.0.0.1:3000`, Roger only `127.0.0.1:9160`. The C# session owns both
processes, redirects their stdin so Vite cannot steal the menu's keyboard input,
and closes them on menu exit or Ctrl+C. A conflicting listener causes an error;
other processes are never terminated to free a port.

The optional upstream GitHub/Firebase features remain upstream features; they are
not required or invoked by the ANK workflow. Opening and editing local trios uses
the local editor, compiler worker and local Roger. Export remains the editor's own
Penrose SVG download. An added Import / Export panel button saves the current SVG and
the three source texts directly to the selected workspace diagram. It reads upstream
Recoil state and the displayed SVG, without changing Penrose's compiler or layout engine.
Vite proxies `/ank/` to a session-owned loopback C# HttpListener with a private session
token. The API accepts a discovered diagram ID, never an arbitrary destination path.
It validates with the official CLI and rsvg before saving private source snapshots,
the Trio descriptor and edited SVG, with previous files archived in `.history/`.

See [the user workflow](../../docs/PENROSE.md) for source/edited SVG priority, history,
and the limits of restoring drag positions from exported source metadata.

The configuration and source are pinned; changes to dependencies must update the
lockfile and be checked against both the browser build and the Roger protocol.
