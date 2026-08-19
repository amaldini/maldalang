#!/usr/bin/env python3
"""Rewrite repo-relative .md hrefs in the GitHub Pages HTML tree.

The published site is only the Reference Manual HTML/CSS/JS/JSON. Relative
hrefs such as ../docs/start-here.md therefore resolve *above* the project Pages
root (https://<user>.github.io/docs/...) and 404. This script rewrites those
hrefs in the copied site to GitHub blob URLs. Source files in git keep the
relative paths so a clone still opens the markdown next to the HTML.
"""

from __future__ import annotations

import argparse
import os
import pathlib
import re
import sys

HREF_MD = re.compile(
    r'href="(?P<href>[^"]+\.md)(?P<frag>#[^"]*)?"',
    re.IGNORECASE,
)


def is_absolute_href(href: str) -> bool:
    return re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", href) is not None


def repo_relative_markdown(source_html: pathlib.Path, href: str, repo_root: pathlib.Path) -> pathlib.Path:
    target = (source_html.parent / href).resolve()
    return target.relative_to(repo_root.resolve())


def rewrite_html(
    html: str,
    source_html: pathlib.Path,
    repo_root: pathlib.Path,
    blob_base: str,
    errors: list[str],
) -> str:
    def repl(match: re.Match[str]) -> str:
        href = match.group("href")
        frag = match.group("frag") or ""
        if is_absolute_href(href):
            return match.group(0)
        try:
            rel = repo_relative_markdown(source_html, href, repo_root)
        except ValueError:
            errors.append(f"{source_html}: {href} escapes the repository")
            return match.group(0)
        target = (repo_root / rel).resolve()
        if not target.is_file():
            errors.append(f"{source_html}: {href} -> missing {rel.as_posix()}")
            return match.group(0)
        return f'href="{blob_base}{rel.as_posix()}{frag}"'

    return HREF_MD.sub(repl, html)


def source_for(site_file: pathlib.Path, site_root: pathlib.Path, source_root: pathlib.Path) -> pathlib.Path:
    return source_root / site_file.relative_to(site_root)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", required=True, type=pathlib.Path, help="Copied Pages tree (e.g. _site)")
    parser.add_argument(
        "--source",
        default=pathlib.Path("ReferenceManual"),
        type=pathlib.Path,
        help="Original ReferenceManual directory used to resolve relative hrefs",
    )
    parser.add_argument(
        "--repo",
        default=os.environ.get("GITHUB_REPOSITORY", ""),
        help="GitHub owner/name (defaults to GITHUB_REPOSITORY)",
    )
    parser.add_argument(
        "--ref",
        default=os.environ.get("PAGES_REF") or os.environ.get("GITHUB_REF_NAME") or "main",
        help="Branch or SHA for blob URLs (defaults to PAGES_REF, GITHUB_REF_NAME, or main)",
    )
    parser.add_argument(
        "--repo-root",
        default=pathlib.Path(__file__).resolve().parents[1],
        type=pathlib.Path,
        help="Repository root (for resolving ../docs links)",
    )
    args = parser.parse_args()

    if not args.repo or "/" not in args.repo:
        print("Need --repo owner/name (or GITHUB_REPOSITORY).", file=sys.stderr)
        return 2

    site_root = args.site.resolve()
    source_root = args.source.resolve()
    repo_root = args.repo_root.resolve()
    if not site_root.is_dir():
        print(f"Missing site directory {site_root}", file=sys.stderr)
        return 1

    blob_base = f"https://github.com/{args.repo}/blob/{args.ref}/"
    errors: list[str] = []
    rewritten = 0

    for html_path in sorted(site_root.rglob("*.html")):
        source_html = source_for(html_path, site_root, source_root)
        original = html_path.read_text(encoding="utf-8")
        updated = rewrite_html(original, source_html, repo_root, blob_base, errors)
        if updated != original:
            html_path.write_text(updated, encoding="utf-8", newline="\n")
            rewritten += 1

    if errors:
        print("Broken markdown links in the Reference Manual:", file=sys.stderr)
        for item in errors:
            print(f"  {item}", file=sys.stderr)
        return 1

    print(f"Rewrote markdown hrefs to {blob_base} in {rewritten} HTML files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
