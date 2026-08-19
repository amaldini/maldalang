#!/usr/bin/env python3
"""Publish the MALDA announcement blog post to Telegraph (not GitHub Pages).

Source of truth: docs/announcement.md (long-form blog + comparison table).

Telegraph pages can be created without a project account. To *update* a page
later, set TELEGRAPH_ACCESS_TOKEN (GitHub Actions repository secret). The
public URL is stored in docs/blog/published.json (never the access token).
"""

from __future__ import annotations

import html as html_lib
import json
import os
import pathlib
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from html.parser import HTMLParser

REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]
ANNOUNCEMENT = REPO_ROOT / "docs" / "announcement.md"
PUBLISHED = REPO_ROOT / "docs" / "blog" / "published.json"
TELEGRAPH_API = "https://api.telegra.ph"
BLOB_BASE = "https://github.com/amaldini/maldalang/blob/main"
TITLE = "MALDA: prompts, tools and agents as language constructs"
AUTHOR_NAME = "Andrea Maldini"
AUTHOR_URL = "https://github.com/amaldini/maldalang"

ALLOWED_TAGS = {
    "a",
    "aside",
    "b",
    "blockquote",
    "br",
    "code",
    "em",
    "figcaption",
    "figure",
    "h3",
    "h4",
    "hr",
    "i",
    "iframe",
    "img",
    "li",
    "ol",
    "p",
    "pre",
    "s",
    "strong",
    "u",
    "ul",
    "video",
}
REMAP_TAGS = {
    "h1": "h3",
    "h2": "h3",
    "h5": "h4",
    "h6": "h4",
    "b": "strong",
    "i": "em",
}
VOID_TAGS = {"br", "hr", "img"}
UNWRAP_TAGS = {"div", "span", "section", "article", "html", "body", "table", "thead", "tbody", "tr"}


def extract_article(markdown: str) -> str:
    start = markdown.find("### MALDA: prompts, tools and agents as language constructs")
    if start < 0:
        raise SystemExit("Could not find the blog-post heading in docs/announcement.md")
    body = markdown[start:]
    body = re.sub(
        r"^### MALDA: prompts, tools and agents as language constructs\s*",
        "",
        body,
        count=1,
        flags=re.MULTILINE,
    )
    body = re.sub(
        r"^## 3\. Comparison with other languages and stacks\s*$",
        "## Comparison with other languages and stacks",
        body,
        count=1,
        flags=re.MULTILINE,
    )
    body = rewrite_relative_links(body)
    header = (
        "MALDA is an open-source programming language where LLM prompts, tools, "
        "agents, HTTP endpoints and durable workflows are syntax instead of library "
        "glue. This is the long-form announcement; the public core is on GitHub at "
        "[amaldini/maldalang](https://github.com/amaldini/maldalang).\n\n"
    )
    footer = (
        "\n\n---\n\n"
        "*Canonical source: "
        f"[docs/announcement.md]({BLOB_BASE}/docs/announcement.md) "
        "in the MALDA repository. Dual MIT OR Apache-2.0. "
        "The name MALDA is a trademark of Andrea Maldini.*\n"
    )
    return header + body.strip() + footer


def rewrite_relative_links(markdown: str) -> str:
    def repl(match: re.Match[str]) -> str:
        text, href = match.group(1), match.group(2)
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", href):
            return match.group(0)
        if href.startswith("#"):
            return match.group(0)
        path = href.split("#", 1)[0]
        frag = "#" + href.split("#", 1)[1] if "#" in href else ""
        if path.startswith("releases/"):
            resolved = "docs/" + path
        else:
            resolved = os.path.normpath(str(pathlib.PurePosixPath("docs") / path))
        return f"[{text}]({BLOB_BASE}/{resolved}{frag})"

    return re.sub(r"\[([^\]]+)\]\(([^)]+)\)", repl, markdown)


def tables_to_lists(markdown: str) -> str:
    lines = markdown.splitlines()
    out: list[str] = []
    i = 0
    while i < len(lines):
        if _is_table_row(lines[i]) and i + 1 < len(lines) and _is_table_sep(lines[i + 1]):
            rows = []
            while i < len(lines) and _is_table_row(lines[i]):
                rows.append(_split_row(lines[i]))
                i += 1
                if i < len(lines) and _is_table_sep(lines[i]):
                    i += 1
            if not rows:
                continue
            headers = rows[0]
            for row in rows[1:]:
                title = row[0] if row else ""
                out.append(f"- **{title.strip('* ')}**")
                for idx, cell in enumerate(row[1:], start=1):
                    label = headers[idx] if idx < len(headers) else f"Col {idx + 1}"
                    out.append(f"  - {label.strip()}: {cell.strip()}")
                out.append("")
            continue
        out.append(lines[i])
        i += 1
    return "\n".join(out)


def _is_table_row(line: str) -> bool:
    stripped = line.strip()
    return stripped.startswith("|") and stripped.endswith("|") and not _is_table_sep(line)


def _is_table_sep(line: str) -> bool:
    stripped = line.strip()
    return bool(re.match(r"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", stripped))


def _split_row(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def markdown_to_html(markdown: str) -> str:
    text = tables_to_lists(markdown)
    fences: list[str] = []

    def save_fence(match: re.Match[str]) -> str:
        code = match.group(2).strip("\n")
        fences.append(f"<pre>{html_lib.escape(code)}</pre>")
        return f"\n\n<!--FENCE{len(fences) - 1}-->\n\n"

    text = re.sub(r"```(\w*)\n(.*?)```", save_fence, text, flags=re.DOTALL)

    chunks = re.split(r"\n\s*\n", text.strip())
    html_parts: list[str] = []
    for chunk in chunks:
        chunk = chunk.strip("\n")
        if not chunk.strip():
            continue
        fence = re.fullmatch(r"<!--FENCE(\d+)-->", chunk.strip())
        if fence:
            html_parts.append(fences[int(fence.group(1))])
            continue
        if re.fullmatch(r"-{3,}", chunk.strip()):
            html_parts.append("<hr>")
            continue
        heading = re.match(r"^(#{2,6})\s+(.*)$", chunk.split("\n", 1)[0])
        if heading:
            level = min(len(heading.group(1)), 4)
            tag = "h3" if level <= 3 else "h4"
            html_parts.append(f"<{tag}>{inline_to_html(heading.group(2).strip())}</{tag}>")
            rest = chunk.split("\n", 1)[1].strip() if "\n" in chunk else ""
            if rest:
                chunk = rest
            else:
                continue
        if not chunk.strip():
            continue
        if _is_list_chunk(chunk):
            html_parts.append(_list_to_html(chunk))
            continue
        html_parts.append(f"<p>{inline_to_html(_join_wrapped_lines(chunk))}</p>")
    return "".join(html_parts)


def _is_list_chunk(chunk: str) -> bool:
    first = chunk.lstrip()
    return first.startswith("- ") or first.startswith("* ")


def _join_wrapped_lines(chunk: str) -> str:
    return re.sub(r"\s*\n\s*", " ", chunk.strip())


def _list_to_html(chunk: str) -> str:
    items: list[dict] = []
    current: dict | None = None
    for line in chunk.splitlines():
        nested = re.match(r"^\s{2,}[-*]\s+(.*)$", line)
        top = re.match(r"^[-*]\s+(.*)$", line)
        if top:
            if current:
                items.append(current)
            current = {"text": top.group(1), "children": []}
        elif nested and current is not None:
            current["children"].append(nested.group(1).strip())
        elif current is not None:
            current["text"] += " " + line.strip()
    if current:
        items.append(current)
    parts: list[str] = []
    for item in items:
        inner = inline_to_html(item["text"])
        if item["children"]:
            kids = "".join(f"<li>{inline_to_html(child)}</li>" for child in item["children"])
            inner += f"<ul>{kids}</ul>"
        parts.append(f"<li>{inner}</li>")
    return f"<ul>{''.join(parts)}</ul>"


INLINE_TOKEN = re.compile(
    r"(`[^`]+`)"
    r"|(\*\*[^*]+?\*\*)"
    r"|(\*[^*\n]+?\*)"
    r"|(\[[^\]]+\]\([^)]+\))"
)


def inline_to_html(text: str) -> str:
    parts: list[str] = []
    pos = 0
    for match in INLINE_TOKEN.finditer(text):
        if match.start() > pos:
            parts.append(html_lib.escape(text[pos : match.start()]))
        raw = match.group(0)
        if raw.startswith("`"):
            parts.append(f"<code>{html_lib.escape(raw[1:-1])}</code>")
        elif raw.startswith("**"):
            parts.append(f"<strong>{inline_to_html(raw[2:-2])}</strong>")
        elif raw.startswith("*"):
            parts.append(f"<em>{inline_to_html(raw[1:-1])}</em>")
        else:
            label, href = re.match(r"\[([^\]]+)\]\(([^)]+)\)", raw).groups()
            parts.append(f'<a href="{html_lib.escape(href, quote=True)}">{inline_to_html(label)}</a>')
        pos = match.end()
    if pos < len(text):
        parts.append(html_lib.escape(text[pos:]))
    return "".join(parts)


class TelegraphHTMLParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.root: dict = {"tag": "root", "children": []}
        self.stack = [self.root]

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        tag = REMAP_TAGS.get(tag, tag)
        if tag in UNWRAP_TAGS:
            return
        if tag == "td" or tag == "th":
            return
        if tag not in ALLOWED_TAGS:
            return
        node: dict = {"tag": tag}
        attr_map = {k: v for k, v in attrs if v is not None}
        if tag == "a" and "href" in attr_map:
            node["attrs"] = {"href": attr_map["href"]}
        elif tag == "img" and "src" in attr_map:
            node["attrs"] = {"src": attr_map["src"]}
            if "alt" in attr_map:
                node["attrs"]["alt"] = attr_map["alt"]
        if tag not in VOID_TAGS:
            node["children"] = []
        self.stack[-1].setdefault("children", []).append(node)
        if tag not in VOID_TAGS:
            self.stack.append(node)

    def handle_endtag(self, tag: str) -> None:
        tag = REMAP_TAGS.get(tag, tag)
        if tag in UNWRAP_TAGS or tag in VOID_TAGS or tag not in ALLOWED_TAGS:
            return
        for i in range(len(self.stack) - 1, 0, -1):
            if self.stack[i].get("tag") == tag:
                self.stack[:] = self.stack[:i]
                break

    def handle_data(self, data: str) -> None:
        if not data:
            return
        parent = self.stack[-1]
        children = parent.setdefault("children", [])
        if parent.get("tag") == "pre":
            children.append(data)
            return
        if data.strip() == "" and not children:
            return
        children.append(data)


def html_to_nodes(html: str) -> list:
    parser = TelegraphHTMLParser()
    parser.feed(html)
    parser.close()
    return _compact(parser.root.get("children") or [])


def _compact(nodes: list) -> list:
    cleaned: list = []
    for node in nodes:
        if isinstance(node, str):
            if node:
                cleaned.append(node)
            continue
        children = _compact(node.get("children") or [])
        tag = node.get("tag")
        if tag == "p" and not children:
            continue
        if tag == "li" and children and isinstance(children[0], dict) and children[0].get("tag") == "p":
            inner = children[0].get("children") or []
            children = inner + children[1:]
        out = {"tag": tag}
        if "attrs" in node:
            out["attrs"] = node["attrs"]
        if tag not in VOID_TAGS:
            out["children"] = children
        cleaned.append(out)
    return cleaned


def markdown_to_nodes(markdown: str) -> list:
    html = markdown_to_html(markdown)
    nodes = html_to_nodes(html)
    if not nodes:
        raise SystemExit("Article converted to zero Telegraph nodes")
    encoded = json.dumps(nodes, ensure_ascii=False)
    if len(encoded.encode("utf-8")) > 64_000:
        raise SystemExit(f"Telegraph content is {len(encoded)} bytes; limit is 64KB")
    return nodes


def load_published() -> dict:
    if PUBLISHED.is_file():
        return json.loads(PUBLISHED.read_text(encoding="utf-8"))
    return {}


def write_published(data: dict) -> None:
    PUBLISHED.parent.mkdir(parents=True, exist_ok=True)
    PUBLISHED.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def telegraph_call(method: str, fields: dict) -> dict:
    url = f"{TELEGRAPH_API}/{method}"
    data = urllib.parse.urlencode(fields).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        method="POST",
        headers={
            "Content-Type": "application/x-www-form-urlencoded",
            "User-Agent": "malda-article-publisher/1.0 (+https://github.com/amaldini/maldalang)",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            raw = resp.read().decode("utf-8")
    except urllib.error.HTTPError as err:
        detail = err.read().decode("utf-8", errors="replace")
        raise SystemExit(f"POST {url} failed: HTTP {err.code}\n{detail}") from err
    except urllib.error.URLError as err:
        raise SystemExit(f"POST {url} failed: {err}") from err
    parsed = json.loads(raw)
    if not parsed.get("ok"):
        raise SystemExit(f"Telegraph {method} failed: {parsed}")
    return parsed["result"]


def ensure_access_token() -> tuple[str, bool]:
    token = os.environ.get("TELEGRAPH_ACCESS_TOKEN")
    if token:
        return token, False
    account = telegraph_call(
        "createAccount",
        {
            "short_name": "malda",
            "author_name": AUTHOR_NAME,
            "author_url": AUTHOR_URL,
        },
    )
    token = account["access_token"]
    token_path = pathlib.Path(os.environ.get("TELEGRAPH_TOKEN_FILE", "/tmp/telegraph-access-token.txt"))
    token_path.write_text(
        "TELEGRAPH_ACCESS_TOKEN=" + token + "\n"
        + "TELEGRAPH_AUTH_URL=" + account.get("auth_url", "") + "\n",
        encoding="utf-8",
    )
    print(
        f"Created a Telegraph account. Credentials written to {token_path} (not for git).",
        file=sys.stderr,
    )
    return token, True


def publish_telegraph(nodes: list, token: str, path: str | None) -> dict:
    fields = {
        "access_token": token,
        "title": TITLE,
        "author_name": AUTHOR_NAME,
        "author_url": AUTHOR_URL,
        "content": json.dumps(nodes, ensure_ascii=False),
    }
    if path:
        fields["path"] = path
        return telegraph_call("editPage", fields)
    return telegraph_call("createPage", fields)


def main() -> int:
    article = extract_article(ANNOUNCEMENT.read_text(encoding="utf-8"))
    nodes = markdown_to_nodes(article)
    if "--check" in sys.argv:
        print(f"ok: {len(nodes)} nodes, {len(json.dumps(nodes).encode())} bytes")
        return 0
    recorded = load_published()
    path = os.environ.get("TELEGRAPH_PAGE_PATH") or recorded.get("path")
    token = os.environ.get("TELEGRAPH_ACCESS_TOKEN")
    if path and not token:
        print(
            f"Article already published as {recorded.get('url') or path}. "
            "Set TELEGRAPH_ACCESS_TOKEN to update it. "
            "Skipping so this run does not create a duplicate page.",
            file=sys.stderr,
        )
        return 0
    created_account = False
    if not token:
        token, created_account = ensure_access_token()
    result = publish_telegraph(nodes, token, path if token and path else None)
    public = {
        "platform": "telegraph",
        "title": result.get("title") or TITLE,
        "path": result["path"],
        "url": result["url"],
        "source": "docs/announcement.md",
    }
    write_published(public)
    action = "updated" if path else "created"
    print(f"{action} {result['url']}")
    if created_account:
        print(
            "Save TELEGRAPH_ACCESS_TOKEN as a GitHub Actions secret so later "
            "pushes can update this article.",
            file=sys.stderr,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
