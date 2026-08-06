# Contributing to MALDA

Thanks for contributing.

By participating, you agree to follow the [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).

For a fuller map (architecture, edit locations, agent hard rules), start with [`AGENTS.md`](AGENTS.md) and [`docs/architecture.md`](docs/architecture.md).

## Good first contributions

The easiest on-ramp is the **Web IDE** (`MaldaLang.IDE`) — Monaco UX, examples browser,
diagnostics presentation, and other playground polish. It is intentionally not
feature-parity with the Desktop IDE; do not assume Desktop-only features (virtual
`@malda-section` tabs, MCP UI, local model browser, UIHost preview) already exist on Web.

Other welcome first issues: clarifying README/docs, fixing small example bugs, and
filtered tests that match a narrow change. Open an issue before a large design change.

## Development setup

1. Install the .NET 8 SDK.
2. Clone this repository.
3. Build:

```bash
dotnet build MaldaLang.sln
```

4. Run a smoke example:

```bash
dotnet run --project MaldaLang -- Examples/Basics/hello_world.malda
```

## Tests

Do **not** run the full test suite unless you intentionally need it — it is large and slow.

Prefer filtered runs:

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~BuiltInRegistryTests"
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~OptionalPackTranspileEmitTests"
```

## Coding notes

- Prefer fixing behavior in MALDA sources (`.malda`) over editing generated artifacts such as `GeneratedProgram.cs` or generated `.js`.
- Prefer the `function` keyword (not `fn` / `def`) in examples and docs.
- Prompt declarations use name-only parameters (no typed prompt params).

## IDEs

- **Desktop IDE** is the reference IDE (Windows). Prefer it when validating full editor workflows.
- **Web IDE** is a browser playground (learn/run/debug). See [Good first contributions](#good-first-contributions) above.

## Pull requests

- Keep changes focused and explained.
- Add or update a small filtered test when fixing language/runtime behavior.
- Update the reference manual chapter source HTML under `ReferenceManual/` when changing documented language behavior. For how the manual is styled, highlighted and printed to paper, see [`ReferenceManual/README-print.md`](ReferenceManual/README-print.md).

## Licensing

MALDA is dual licensed under the [MIT License](LICENSE-MIT) or the
[Apache License 2.0](LICENSE-APACHE), at the recipient's option. This is the
arrangement Rust popularised, and it exists because the two licences suit different
adopters: MIT is the shortest permissive licence and stays compatible with GPLv2,
while Apache-2.0 carries an express patent grant with a retaliation clause and an
explicit trademark disclaimer. Nobody has to justify their choice; they just pick one.

Whichever is chosen covers the whole repository: the runtime and compiler, the IDEs
and tooling, the reference manual under `ReferenceManual/`, the documentation under
`docs/`, and the sample programs under `Examples/`. MIT explicitly grants rights over
"the software **and associated documentation files**", and Apache-2.0 defines its
"Work" broadly enough to do the same, so the documentation needs no separate licence.

Code examples in the manual and in `Examples/` carry the same dual offer, which means
you can paste them into your own projects — including commercial ones — without
attribution obligations beyond what your chosen licence already states.

The SPDX expression for the project as a whole is `MIT OR Apache-2.0`.

### Programs compiled with MALDA

The toolchain does not only translate your program: the C# transpiler emits its
`RuntimeHelpers` class (around 1500 lines) into every generated program, inlines the
UI host runtime for programs that use `UIHost`, and the JavaScript backend writes
`malda-js-runtime.js` next to the generated output. Read strictly, MIT's requirement
to reproduce the notice in "substantial portions of the Software" — and Apache-2.0's
section 4 notice and statement-of-changes duties — could be taken to follow that
runtime code into everyone's binaries.

[`LICENSE-RUNTIME-EXCEPTION`](LICENSE-RUNTIME-EXCEPTION) says plainly that they do
not. Programs compiled with MALDA belong to whoever wrote them and can be licensed on
any terms, with no obligation to credit MALDA on account of the injected runtime.
Redistributing MALDA *itself* is a different matter and stays under whichever of the
two licences the redistributor chose.

If you change what the transpilers emit into user programs, check whether the
"Runtime Material" list in that file still describes reality.

### Contributions

By submitting a pull request you agree that your contribution is dual licensed under
the MIT License **and** the Apache License 2.0, on the same terms as the project, and
you confirm you have the right to license it that way. This is the usual
inbound-equals-outbound arrangement, so there is no CLA to sign and no separate
paperwork.

The "and" matters: contributions must be available under *both* licences, because a
recipient who chooses Apache-2.0 has to receive the whole work under Apache-2.0. A
contribution offered under only one of the two would break the dual offer for
everything downstream of it.

If your change includes code you did not write — a snippet from another project, a
vendored library, a generated file with its own header — say so in the pull request
and record the origin and licence in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
Do not import code under a copyleft licence (GPL, AGPL, LGPL) or under no licence
at all.

### File headers

New C# files should start with these two lines:

```csharp
// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
```

The SPDX line is machine-readable, which is what licence scanners look for, and the
`OR` is what tells them the recipient chooses. Apache-2.0 suggests a longer boilerplate
header in its appendix; the one-line SPDX form is the accepted modern equivalent and is
what Rust and most current projects use.

A file with no header at all is also fine — the root licences cover the whole
repository, and other file types (`.malda`, `.css`, `.js`, `.ps1`, Markdown) carry no
per-file header by convention.

What is *not* fine is a header that contradicts the licence. `LicenseHeaderGuardTests`
fails if any file reintroduces the phrase "All rights reserved", or if a C# file
carries a copyright line without the matching SPDX line. That phrase asserts the
opposite of the grant, and the mismatch trips the automated licence scanners many
organisations run before adopting a dependency. The same test also checks that both
licence files and the runtime exception are still in place.
