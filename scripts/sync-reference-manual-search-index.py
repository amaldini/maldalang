#!/usr/bin/env python3
"""Generate heading indexes and embed glossary fallbacks for the Reference Manual search UI.

Sources of truth:
  - ReferenceManual/glossary.json
  - ReferenceManual/it/glossary.json
  - numbered chapter HTML (h2 / distinctive h3)

Writes:
  - ReferenceManual/headings.json
  - ReferenceManual/it/headings.json
  - FALLBACK_GLOSSARY_* blocks in ReferenceManual/navigation.js
"""

from __future__ import annotations

import json
import pathlib
import re
import sys
from html import unescape

REPO = pathlib.Path(__file__).resolve().parents[1]
MANUAL = REPO / "ReferenceManual"
NAV = MANUAL / "navigation.js"

HEADING_RE = re.compile(r"<h([23])([^>]*)>(.*?)</h\1>", re.S)
ID_RE = re.compile(r'id="([^"]+)"')
SKIP_TITLES = {
    "see also",
    "example",
    "examples",
    "constructor",
    "methods",
    "syntax",
    "behavior",
    "complete example",
    "use cases",
}


def strip_tags(inner: str) -> str:
    plain = unescape(re.sub(r"<[^>]+>", "", inner))
    return re.sub(r"\s+", " ", plain).strip()


def extract_headings(folder: pathlib.Path) -> list[dict]:
    headings: list[dict] = []
    for path in sorted(folder.glob("[0-9][0-9]-*.html")):
        text = path.read_text(encoding="utf-8")
        for match in HEADING_RE.finditer(text):
            level = int(match.group(1))
            attrs = match.group(2)
            title = strip_tags(match.group(3))
            if not title:
                continue
            hid_match = ID_RE.search(attrs)
            hid = hid_match.group(1) if hid_match else ""
            lowered = title.lower()
            numbered = bool(re.match(r"\d+\.", title))
            if lowered in SKIP_TITLES and not hid and not numbered:
                continue
            if level == 3 and not hid and not numbered:
                continue
            headings.append(
                {
                    "file": path.name,
                    "level": level,
                    "id": hid,
                    "title": title,
                }
            )
    return headings


def load_glossary(path: pathlib.Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    return data["terms"]


def compact_terms(terms: list[dict]) -> list[dict]:
    compact = []
    for term in terms:
        item = {
            "id": term["id"],
            "term": term["term"],
            "aliases": term.get("aliases") or [],
            "href": term["href"],
            "summary": term.get("summary") or "",
        }
        if term.get("also"):
            item["also"] = term["also"]
        compact.append(item)
    return compact


def replace_fallback(source: str, const_name: str, terms: list[dict]) -> str:
    marker = f"const {const_name} = "
    start = source.find(marker)
    if start < 0:
        raise SystemExit(f"Could not find {const_name} in navigation.js")
    array_start = source.find("[", start)
    if array_start < 0:
        raise SystemExit(f"Could not find array start for {const_name}")
    end = source.find("\n];", array_start)
    if end < 0:
        raise SystemExit(f"Could not find array end for {const_name}")
    end += len("\n];")
    payload = json.dumps(terms, ensure_ascii=False, indent=4)
    # Keep the JS identifier; JSON objects are valid JS here (no undefined).
    block = f"const {const_name} = {payload};"
    return source[:start] + block + source[end:]


def write_json(path: pathlib.Path, value) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    en_terms = compact_terms(load_glossary(MANUAL / "glossary.json"))
    it_terms = compact_terms(load_glossary(MANUAL / "it" / "glossary.json"))
    en_headings = extract_headings(MANUAL)
    it_headings = extract_headings(MANUAL / "it")

    write_json(MANUAL / "headings.json", en_headings)
    write_json(MANUAL / "it" / "headings.json", it_headings)

    nav = NAV.read_text(encoding="utf-8")
    nav = replace_fallback(nav, "FALLBACK_GLOSSARY_EN", en_terms)
    nav = replace_fallback(nav, "FALLBACK_GLOSSARY_IT", it_terms)
    NAV.write_text(nav, encoding="utf-8")

    print(f"Wrote {len(en_headings)} EN headings, {len(it_headings)} IT headings")
    print(f"Embedded {len(en_terms)} EN glossary terms and {len(it_terms)} IT terms")
    return 0


if __name__ == "__main__":
    sys.exit(main())
