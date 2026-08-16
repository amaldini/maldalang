#!/usr/bin/env python3
"""Rewrite asset and docs paths in ReferenceManual/it so they stay valid.

Italian pages live one directory deeper than the English canonical files.
Idempotent: safe to run after a translation pass.
"""

from __future__ import annotations

import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
IT_DIR = REPO / "ReferenceManual" / "it"


def rewrite(html: str) -> str:
    html = re.sub(r'<html lang="en">', '<html lang="it">', html, count=1)
    replacements = (
        ('href="styles.css"', 'href="../styles.css"'),
        ('href="syntax.css"', 'href="../syntax.css"'),
        ('href="print.css"', 'href="../print.css"'),
        ('src="malda-highlight.js"', 'src="../malda-highlight.js"'),
        ('src="navigation.js"', 'src="../navigation.js"'),
        ('src="index-toc.js"', 'src="../index-toc.js"'),
        ('href="../docs/', 'href="../../docs/'),
    )
    for old, new in replacements:
        html = html.replace(old, new)
    return html


def main() -> int:
    if not IT_DIR.is_dir():
        print(f"Missing {IT_DIR}", file=sys.stderr)
        return 1

    updated = 0
    for path in sorted(IT_DIR.glob("*.html")):
        original = path.read_text(encoding="utf-8")
        rewritten = rewrite(original)
        if rewritten != original:
            path.write_text(rewritten, encoding="utf-8", newline="\n")
            print(f"Updated {path.name}")
            updated += 1

    print(f"Done. {updated} file(s) updated.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
