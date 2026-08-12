# Workspace packages

**Status:** Shipped (P2 maturity roadmap)  
**Parent:** [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md)  
**Example:** [`Examples/Modules/workspace_package.malda`](../Examples/Modules/workspace_package.malda)

Local/workspace libraries under `packages/` — **no public registry hub** in this project.

## Preferred workflow

1. Create `packages/<name>/` with an entry `.malda` file and optional `package.json`.
2. From a cwd inside the repo (or with `MALDA_PACKAGES_DIR` set), import:

```malda
import malda-demo-math;
import { clamp, VERSION } from malda-demo-math;
```

3. No `malda install` is required for workspace resolution.

OSS demo pack: [`packages/malda-demo-math/`](../packages/malda-demo-math/).

## Resolution order

`ModuleResolver` resolves a package name as:

1. **Installed store** — `~/.maldalang/packages/{name}/{version}/` (wins if present)
2. **Workspace fallback** — [`WorkspacePackageResolver`](../MaldaLang/PackageManager/WorkspacePackageResolver.cs)

Workspace roots (first match wins per name), in order:

| Source | Meaning |
|--------|---------|
| `MALDA_PACKAGES_DIR` | Directory that *contains* package folders |
| `MALDA_SDK_ROOT/packages` | Or `MALDA_SDK_ROOT` if it is itself a packages root |
| Walk-up from cwd | Look for `packages/` up to 12 parent levels |

### Entry candidates

For package `malda-foo`:

1. `foo.malda` (short name after `malda-` prefix)
2. `index.malda`
3. `main.malda`
4. `{name}.malda`
5. Else first `lib/*.malda`

Submodules: `lib/{sub}.malda` or `{sub}.malda` under the package folder.

## CLI (offline-first)

| Command | Registry? | Role |
|---------|-----------|------|
| `malda list --workspace` | No | List packages visible from workspace roots |
| `malda install <local-path>` | No | Copy a local folder/`package.json`/`.malda` into `~/.maldalang/packages` |
| `malda list` / `uninstall` / `init` | No | Manage installed store / scaffold `package.json` |
| `malda install <name>[@ver]` | Yes (`MALDA_REGISTRY_URL`) | Remote fetch |
| `malda search <query>` | Yes | Remote search |

There is **no** project-hosted public npm-like registry. Remote commands are optional for private/self-hosted registries only.

## Selective imports

Named imports from a package use the same resolver as full `import pkg;` — see [`docs/selective-imports.md`](selective-imports.md).

## Non-goals (this release)

- Public package registry hosted by the project / `malda publish`
- `module { }` blocks, import rename (`as`) (see `export type` / `export schema` in [`selective-imports.md`](selective-imports.md))
- Vertical domain packs inside this OSS tree
