# Reference Manual — content guards

The manual documents code that keeps changing, so a set of tests keeps the two in
step. All of them live in `MaldaLang.Tests` and run in a few hundred milliseconds:

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~ReferenceManual"
```

## What is guarded

| Test | Guarantee |
|------|-----------|
| `ReferenceManualChapterSyncTests` | Chapter titles, breadcrumbs, and `<h1>` match `chapters.json` order |
| `ReferenceManualGrammarCoverageTests` | `35-grammar.html` names the parser constructs it must cover |
| `ReferenceManualContentGuardTests.ReservedWordLists_CoverEveryLexerKeyword` | Reserved word lists in ch. 3 and the appendix equal `Lexer.Keywords` exactly |
| `ReferenceManualContentGuardTests.EveryRegistryBuiltIn_IsMentionedSomewhereInTheManual` | Every `BuiltInRegistry` name appears somewhere in the manual |
| `ReferenceManualContentGuardTests.InternalLinks_ResolveToExistingPages` | No `href` points at a missing chapter file |
| `ReferenceManualContentGuardTests.MarkdownLinks_ResolveToExistingRepoFiles` | Every `href` to a `.md` file resolves inside the repo |
| `ReferenceManualContentGuardTests.NavigationJs_RewritesMarkdownLinksOnGitHubPages` | `navigation.js` rewrites those `.md` hrefs on `*.github.io` |
| `ReferenceManualContentGuardTests.PagesDeploy_RewritesMarkdownLinksToGitHubBlob` | Pages deploy rewrites copied HTML to GitHub blob URLs |
| `ReferenceManualContentGuardTests.SectionNumbers_AreUniqueWithinEachChapter` | No chapter reuses a section number such as two `35.5` headings |
| `ReferenceManualContentGuardTests.WebUiChapter_NamesEveryRegisteredControlType` | Every `UiControlSpecRegistry` control type appears as `ui.<type>` in `24-web-ui.html` |
| `ReferenceManualContentGuardTests.NavigationFallback_MatchesChaptersJson` | `FALLBACK_NAV_ITEMS` in `navigation.js` matches `chapters.json` |
| `ReferenceManualContentGuardTests.IndexTocFallback_MatchesChaptersJson` | `FALLBACK_TOC_CHAPTERS` in `index-toc.js` matches `chapters.json` |
| `ReferenceManualContentGuardTests.ChapterCategories_AreContiguousInReadingOrder` | Each menu category is a contiguous number range in `chapters.json` |
| `ReferenceManualContentGuardTests.NavAndTocCategoryOrder_ListsEveryChapterCategory` | Sidebar and home TOC category lists match first-seen `chapters.json` categories |
| `ReferenceManualContentGuardTests.ChapterFilenames_MatchDisplayNumbers` | Each numbered chapter file starts with `{nn}-` matching its `chapters.json` order |
| `ReferenceManualContentGuardTests.ChapterMastheads_MatchCliVersion` | Chapter headers, the Tools REPL banner, the home “What ships today” line, and the book script stamp the CLI `<Version>` |
| `ReferenceManualRunnableSnippetTests` | Every snippet marked runnable actually runs and prints what the manual claims |
| `ReferenceManualItalianTests` | Italian tree in `it/` mirrors English files, keeps code listings identical, uses `../` assets / `../../docs/` links, and `STATUS.md` matches English SHA-256 |
| `ReferenceManualChapterSyncTests.ItalianManual_ChapterTitlesMatchChaptersJson` | Italian titles, breadcrumbs, and `<h1>` match `it/chapters.json` |

## Adding a built-in

Registering a name in `BuiltInRegistry` and not mentioning it anywhere in
the English `ReferenceManual/*.html` (not `it/`) fails the coverage guard. Either document it, or add the
name to `UndocumentedBuiltInAllowList` in `ReferenceManualContentGuardTests`
together with the reason it should stay undocumented.

Web UI built-ins are registered flat (`uiButton`) but documented under the spelling
users write (`ui.button`); the guard maps between the two automatically.

## Marking a snippet as runnable

Add `data-run="true"` to the `<code>` element. Add `data-expect="..."` to assert the
printed output as well, using `\n` between lines:

```html
<pre><code data-run="true" data-expect="3&#92;nok">const limit = 3;
print(limit);
print("ok");</code></pre>
```

Rules that keep this useful:

- A runnable snippet must be self-contained. It runs through the interpreter with no
  surrounding context, so it cannot reference functions defined in a nearby block.
- Do not mark snippets that need network access, an LLM, a server, user input, or
  files that are not created by the snippet itself.
- Snippets that deliberately show an error should comment out the failing line and
  describe the error in prose, so the rest of the block stays runnable.
- Blocks with no `data-run` attribute are ignored, so fragments and pseudo-code need
  no change.

`ReferenceManualRunnableSnippetTests` also fails if the number of runnable snippets
drops below a floor, which catches attributes lost during a bulk edit.
