#!/usr/bin/env python3
"""Emit namespaced signature tables for Reference Manual chapter 13.3–13.8.

Reads docs/llm/malda-builtins.tsv (name, preferred call, arguments, notes, returns).
Does not rewrite the chapter; used to keep table rows aligned with the TSV.
"""

from __future__ import annotations

import html
import pathlib

REPO = pathlib.Path(__file__).resolve().parents[1]
TSV = REPO / "docs" / "llm" / "malda-builtins.tsv"

HEADERS = {
    "en": ("Call", "Arguments", "Returns", "Notes"),
    "it": ("Chiamata", "Argomenti", "Ritorno", "Note"),
}


def load_rows() -> list[tuple[str, str, str, str, str]]:
    rows = []
    for line in TSV.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        parts = line.split("\t")
        while len(parts) < 5:
            parts.append("")
        rows.append(tuple(parts[:5]))
    return rows


def display_call(name: str, preferred: str, arguments: str) -> str:
    if "(" in preferred:
        return preferred
    hint = ""
    if "(" in arguments:
        start = arguments.find("(")
        end = arguments.rfind(")")
        if end > start:
            hint = arguments[start : end + 1]
    if not hint:
        if arguments.startswith("0 arguments"):
            hint = "()"
        elif arguments.startswith("1 argument") and ":" not in arguments:
            hint = "(x)"
        elif arguments.startswith("2 arguments") and ":" not in arguments:
            hint = "(a, b)"
        elif arguments.startswith("3 arguments") and ":" not in arguments:
            hint = "(x, lo, hi)" if name == "clamp" else "(a, b, c)"
    return preferred + hint


def table(rows: list[tuple[str, str, str, str, str]], locale: str) -> str:
    h1, h2, h3, h4 = HEADERS[locale]
    lines = [
        "<table>",
        "<thead>",
        f"<tr><th>{h1}</th><th>{h2}</th><th>{h3}</th><th>{h4}</th></tr>",
        "</thead>",
        "<tbody>",
    ]
    for name, preferred, arguments, notes, returns in rows:
        call = display_call(name, preferred, arguments)
        lines.append(
            "<tr>"
            f"<td><code>{html.escape(call)}</code></td>"
            f"<td>{html.escape(arguments)}</td>"
            f"<td>{html.escape(returns) if returns else '—'}</td>"
            f"<td>{html.escape(notes)}</td>"
            "</tr>"
        )
    lines.extend(["</tbody>", "</table>"])
    return "\n        ".join(lines)


def select(rows, pred):
    return [r for r in rows if pred(r)]


def main() -> int:
    rows = load_rows()
    groups = {
        "math": select(rows, lambda r: r[1].startswith("math.")),
        "str": select(rows, lambda r: r[1].startswith("str.")),
        "io-files": select(
            rows,
            lambda r: (
                r[1].startswith("io.")
                and not r[0].startswith("git")
                and r[0] not in {"print", "input", "getEnv", "getEnvOr", "hasEnv"}
            )
            or r[0] in {"editFile", "insertAtLine", "extractPdfText", "extractDocxText"},
        ),
        "env": select(
            rows,
            lambda r: r[0]
            in {
                "getEnv",
                "getEnvOr",
                "hasEnv",
                "getHostPlatform",
                "getCommandLineArgs",
                "getProgramDirectory",
                "getMaldaHome",
                "getMaldaConfig",
                "getAssistantMemory",
                "getSkillNames",
                "loadSkill",
                "loadSkillsFromDir",
                "webSearch",
                "webFetch",
                "httpBearerToken",
                "httpCookieToken",
                "httpAuthToken",
            },
        ),
        "arrays": select(rows, lambda r: r[0] in {"reverse", "sort", "includes", "join", "toCsv"}),
        "json": select(
            rows,
            lambda r: r[0] in {"toJSON", "parseJSON", "parseJson", "validate", "asVariant", "evalPrompt"},
        ),
    }
    out = REPO / "artifacts" / "ch13-tables"
    out.mkdir(parents=True, exist_ok=True)
    for locale in ("en", "it"):
        for key, group in groups.items():
            (out / f"{locale}-{key}.html").write_text(table(group, locale) + "\n", encoding="utf-8")
            print(f"{locale}-{key}: {len(group)} rows")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
