#!/usr/bin/env python3
"""Record SHA-256 hashes of the English Reference Manual pages in it/STATUS.md."""

from __future__ import annotations

import hashlib
import json
import pathlib
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
MANUAL = REPO / "ReferenceManual"
IT_DIR = MANUAL / "it"
STATUS = IT_DIR / "STATUS.md"


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    digest.update(path.read_bytes())
    return digest.hexdigest()


def main() -> int:
    chapters_path = IT_DIR / "chapters.json"
    if not chapters_path.is_file():
        print(f"Missing {chapters_path}", file=sys.stderr)
        return 1

    chapters = json.loads(chapters_path.read_text(encoding="utf-8"))["chapters"]
    rows = []
    for chapter in chapters:
        name = chapter["file"]
        en_path = MANUAL / name
        if not en_path.is_file():
            print(f"Missing English source {en_path}", file=sys.stderr)
            return 1
        rows.append((name, sha256(en_path)))

    lines = [
        "# Italian translation status",
        "",
        "English in `ReferenceManual/` is canonical. Each row is the SHA-256 of",
        "the English HTML this Italian page was translated from. After changing",
        "an English chapter, update `it/{file}` and regenerate this table:",
        "",
        "```bash",
        "python3 scripts/sync-reference-manual-it-status.py",
        "```",
        "",
        "| File | EN SHA-256 |",
        "|------|------------|",
    ]
    for name, digest in rows:
        lines.append(f"| {name} | {digest} |")
    lines.append("")

    STATUS.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"Wrote {STATUS} ({len(rows)} files)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
