# Third-party notices

MALDA itself is dual licensed under the [MIT License](LICENSE-MIT) or the
[Apache License 2.0](LICENSE-APACHE), at your option. This file records the
third-party components it depends on, so that anyone redistributing MALDA — as
source, as a binary, or as a printed manual — can see what else travels with it
and under what terms.

Note the file name. Section 4(d) of the Apache License 2.0 obliges redistributors
to carry forward a file literally named `NOTICE`, and MALDA deliberately does not
ship one: this document is informational, and reading it imposes no duty on you.

Licence identifiers were read from the packages actually resolved by the build
(`.nuspec` metadata and the licence files inside the packages), not from memory.
Verified against the versions listed below; re-check when you bump a dependency.

## Summary

Every dependency is under a permissive licence. There is **no copyleft** (GPL,
LGPL, AGPL, MPL) anywhere in the graph, and every licence below is compatible
with redistributing MALDA under either MIT or Apache-2.0, so no dependency
constrains which of the two you choose. The only obligations are preserving the
copyright and licence notices reproduced here.

One component is worth calling out: **Microsoft.Web.WebView2** ships under a
Microsoft BSD-3-Clause-style redistribution licence rather than MIT. It is
permissive, but its third clause forbids using Microsoft's name to endorse
derived products, and it is only pulled in by the Windows Desktop IDE. See the
note in the table below.

## NuGet packages

| Package | Version | Licence | Used by |
|---------|---------|---------|---------|
| AvalonEdit | 6.3.0 | MIT | Desktop IDE (code editor) |
| LLamaSharp | 0.26.0 | MIT | Local model inference |
| LLamaSharp.Backend.Cpu | 0.26.0 | MIT | Local model inference (CPU backend) |
| Markdig | 0.33.0 | BSD-2-Clause | Markdown rendering (`markdownToHtml` builtin; Desktop/Web IDE chat) |
| PdfPig | 0.1.15 | Apache-2.0 | PDF text extraction (`pdf.extractText` / `extractPdfText` builtin) |
| DocumentFormat.OpenXml | 3.5.1 | MIT | Word `.docx` text extraction (`doc.extractText` / `extractDocxText` builtin) |
| DocumentFormat.OpenXml.Framework | 3.5.1 | MIT | Open XML SDK shared framework (dependency of DocumentFormat.OpenXml) |
| System.IO.Packaging | 8.0.1 | MIT | OPC package IO for Open XML (dependency of DocumentFormat.OpenXml) |
| Microsoft.Build | 17.8.3 | MIT | Project/build integration |
| Microsoft.Build.Utilities.Core | 17.8.3 | MIT | Project/build integration |
| Microsoft.Data.SqlClient | 5.2.2 | MIT | SQL Server client |
| Microsoft.Data.Sqlite | 10.0.3 | MIT | SQLite client |
| Microsoft.Extensions.DependencyInjection | 8.0.0 | MIT | Runtime services |
| Microsoft.Extensions.FileSystemGlobbing | 8.0.0 | MIT | Glob built-ins |
| Microsoft.Extensions.Logging | 8.0.0 | MIT | Runtime logging |
| Microsoft.ML.OnnxRuntime | 1.20.1 | MIT | ONNX embeddings / cross-encoder |
| Microsoft.ML.Tokenizers | 0.22.0 | MIT | Tokenisation |
| Microsoft.Web.WebView2 | 1.0.3719.77 | BSD-3-Clause-style Microsoft licence — see note | Desktop IDE (embedded browser) |
| Npgsql | 8.0.5 | PostgreSQL Licence (BSD-style) | PostgreSQL client |
| OmniSharp.Extensions.JsonRpc | 0.19.9 | MIT | Language server |
| OmniSharp.Extensions.LanguageProtocol | 0.19.9 | MIT | Language server |
| OmniSharp.Extensions.LanguageServer | 0.19.9 | MIT | Language server |
| OmniSharp.Extensions.LanguageServer.Shared | 0.19.9 | MIT | Language server |
| Spectre.Console | 0.49.1 | MIT | CLI output |
| System.IO.Ports | 8.0.0 | MIT | Device integration |

Test-only dependencies, not redistributed with the product:

| Package | Version | Licence |
|---------|---------|---------|
| coverlet.collector | 6.0.0 | MIT |
| Microsoft.NET.Test.Sdk | 17.8.0 | MIT |
| xunit | 2.5.3 | Apache-2.0 |
| xunit.runner.visualstudio | 2.5.3 | Apache-2.0 |

### Note on Microsoft.Web.WebView2

The package licence is the BSD three-clause form: redistribution in source or
binary is permitted provided the copyright notice, the conditions and the
disclaimer are retained, and provided Microsoft's name is not used to endorse or
promote derived products without written permission. The full text is in
`LICENSE.txt` inside the package. Note separately that the WebView2 *runtime*
installed on the end user's machine is Microsoft software governed by its own
terms; MALDA does not redistribute it.

## Bundled browser assets

Checked into this repository and served directly, so their licence notices must
be preserved:

| Component | Version | Licence | Location |
|-----------|---------|---------|----------|
| Bootstrap | 5.1.0 | MIT | `MaldaLang.IDE/wwwroot/bootstrap/` |

The Bootstrap copyright banner is retained at the top of the minified CSS, as its
licence requires.

## Assets loaded from a CDN at runtime

Referenced by URL and fetched by the browser. They are **not** redistributed as
part of MALDA, so no notice obligation attaches to a MALDA distribution; they are
listed for completeness and for anyone vendoring them locally.

| Component | Version | Licence | Where |
|-----------|---------|---------|-------|
| Monaco Editor | 0.45.0 | MIT | Web IDE editor, from jsDelivr |
| Paged.js | 0.4.3 | MIT | Reference manual book build, from unpkg |

Both are optional at runtime. The book build degrades gracefully without Paged.js
(`-NoPagedJs`, or simply no network): the manual still prints, without running
heads, folios or contents page numbers.

## Example programs under a different licence

Most examples inherit the repository dual licence. This one does not:

| File | Licence | Notes |
|------|---------|-------|
| `Examples/Games/three_shader_path_tunnel.malda` | CC-BY-NC-SA-4.0 | Conversion of [@Frostbyte's path-marching tunnel](https://fragcoord.xyz/s/tbe1g319). Palette from Inigo Quilez (MIT). Noise from Xor / Fabrice. Non-commercial ShareAlike — do not treat this file as MIT or Apache-2.0. |

The host HTML smoke page next to it is original MALDA boilerplate and stays under the repository licences.

## Fonts

The stylesheets request font *families* by name (Inter, JetBrains Mono, and system
UI and serif stacks) and fall back to whatever the machine provides. No font files
are bundled or downloaded, so no font licence applies to this repository.

## Adding a dependency

Add a row here in the same pull request, with the licence read from the package
rather than assumed. Do not add dependencies under copyleft licences (GPL, LGPL,
AGPL) or with no declared licence — either would change the terms under which
MALDA as a whole can be distributed.
