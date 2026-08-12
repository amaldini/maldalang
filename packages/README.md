# Workspace packages

Local/workspace libraries for MALDA without a public registry hub.

## Layout

```
packages/
  malda-demo-math/
    package.json
    demo-math.malda      # entry for import malda-demo-math;
```

For a package named `malda-foo`, the resolver looks for `foo.malda`, then `index.malda`, `main.malda`, or `{name}.malda` under `packages/{name}/`.

## Resolution

`import malda-demo-math;` / `import { clamp } from malda-demo-math;` resolves when:

1. The package is installed under `~/.maldalang/packages` (wins), or
2. A workspace root is found via, in order:
   - `MALDA_PACKAGES_DIR` (directory that contains package folders)
   - `MALDA_SDK_ROOT/packages` (or `MALDA_SDK_ROOT` if it is itself a packages root)
   - walk-up from the process cwd looking for a `packages/` directory (max 12 levels)

No `malda install` is required for workspace packs.

## Supported workflow

See [`docs/workspace-packages.md`](../docs/workspace-packages.md), [`CONTRIBUTING.md`](../CONTRIBUTING.md), and [`Examples/Modules/workspace_package.malda`](../Examples/Modules/workspace_package.malda).

## Out of scope

This repository does **not** host a public npm-like package registry. Remote `malda install <name>` / `malda search` need an optional `MALDA_REGISTRY_URL` pointing at *your* registry if you have one.
