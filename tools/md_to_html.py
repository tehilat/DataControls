"""Converts docs/submission.md to a styled, printable HTML page (Hebrew, RTL).

Usage (from the repository root):
    python tools/md_to_html.py                                  # docs/submission.md -> docs/submission.html
    python tools/md_to_html.py <input.md> <output.html>

Standard library only. Supports the Markdown subset the submission uses:
headings (#, ##, ###), paragraphs, bullet and numbered lists (with indented continuation lines),
tables, fenced code blocks, inline code, **bold** and [links](url).

Layout rules:
- Everything before the first "##" is the cover page (title, author, date), followed by a generated table of contents.
- Every "##" starts a chapter; in print each chapter begins on a new page. A line "<!-- same-page -->" right before
  a "##" lets a short chapter continue on the current page instead (used for the closing references chapter).
- Other HTML comment lines are ignored.
- Code (blocks and inline) is shown left-to-right inside the right-to-left page.
"""
from __future__ import annotations

import html
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_IN = ROOT / "docs" / "submission.md"
DEFAULT_OUT = ROOT / "docs" / "submission.html"

CSS = """
:root {
  --text: #1b1f27; --muted: #5a6272; --border: #c9d1dc; --head-bg: #eef2f7;
  --accent: #12233d; --code-bg: #f4f6f9;
}
* { box-sizing: border-box; }
body {
  margin: 0; background: #e9edf2; color: var(--text);
  font-family: Heebo, "Segoe UI", Arial, sans-serif; font-size: 11pt; line-height: 1.6;
}
main { max-width: 210mm; margin: 24px auto; background: #fff; padding: 18mm 18mm 20mm; box-shadow: 0 1px 6px rgb(0 0 0 / .15); }
h1, h2, h3 { color: var(--accent); line-height: 1.3; }
h2 { font-size: 17pt; border-bottom: 2px solid var(--accent); padding-bottom: 4px; margin: 0 0 12px; }
h3 { font-size: 12.5pt; margin: 18px 0 6px; }
p { margin: 6px 0 10px; }
ul, ol { margin: 6px 0 10px; padding-inline-start: 22px; }
li { margin: 2px 0; }
a { color: #0b4a8b; }
strong { color: var(--accent); }
table { width: 100%; border-collapse: collapse; margin: 8px 0 14px; font-size: 10pt; }
th, td { border: 1px solid var(--border); padding: 5px 8px; text-align: start; vertical-align: top; }
th { background: var(--head-bg); font-weight: 600; }
tr { break-inside: avoid; }
code {
  font-family: Consolas, "Cascadia Mono", "Courier New", monospace; font-size: 9pt;
  background: var(--code-bg); border: 1px solid #e1e6ee; border-radius: 3px; padding: 0 3px;
  unicode-bidi: isolate;
}
pre {
  direction: ltr; text-align: left; background: var(--code-bg); border: 1px solid var(--border); border-radius: 6px;
  padding: 10px 12px; margin: 8px 0 14px; overflow-x: auto; white-space: pre-wrap; word-break: break-word;
  font-size: 8.6pt; line-height: 1.45; break-inside: avoid;
}
pre code { background: none; border: 0; padding: 0; font-size: inherit; }
.chapter { margin-top: 30px; }

/* cover page */
.cover { min-height: 245mm; display: flex; flex-direction: column; justify-content: center; text-align: center; }
.cover h1 { font-size: 34pt; margin: 0 0 10px; }
.cover .subtitle { font-size: 14pt; color: var(--muted); margin: 0 0 40px; }
.cover .meta p { font-size: 13pt; margin: 4px 0; }
.cover .radar { font-size: 56pt; line-height: 1; margin-bottom: 18px; color: var(--accent); }
.toc { margin: 48px auto 0; text-align: start; max-width: 120mm; }
.toc h2 { font-size: 13pt; border: 0; text-align: center; margin-bottom: 6px; }
.toc ol { list-style: none; padding: 0; } /* chapter titles carry their own numbers */
.toc li { margin: 3px 0; }

@page { size: A4; margin: 14mm 14mm 15mm; }
@media print {
  body { background: #fff; font-size: 10pt; line-height: 1.5; }
  p { margin: 4px 0 7px; }
  h3 { margin: 12px 0 4px; }
  table { font-size: 9.3pt; margin: 6px 0 10px; }
  th, td { padding: 3px 7px; }
  pre { font-size: 8pt; padding: 7px 10px; margin: 6px 0 10px; }
  main { margin: 0; padding: 0; max-width: none; box-shadow: none; }
  .cover { min-height: 255mm; }
  .chapter { margin-top: 0; break-before: page; }
  .chapter.same-page { margin-top: 20px; break-before: auto; }
  h2, h3 { break-after: avoid; }
  a { color: inherit; text-decoration: none; }
}
"""


def inline(text: str) -> str:
    """Escapes HTML and applies `code`, **bold** and [links](url). Code spans are protected from other rules."""
    codes: list[str] = []

    def keep_code(m: re.Match[str]) -> str:
        codes.append(f'<code dir="ltr">{html.escape(m.group(1))}</code>')
        return f"\x00{len(codes) - 1}\x00"

    text = re.sub(r"`([^`]+)`", keep_code, text)
    text = html.escape(text, quote=False)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(
        r"\[([^\]]+)\]\(([^)\s]+)\)",
        lambda m: f'<a href="{html.escape(m.group(2), quote=True)}">{m.group(1)}</a>',
        text,
    )
    return re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], text)


HEBREW = re.compile(r"[֐-׿]")
JSON_STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')


def code_line(line: str) -> str:
    """One line of a code block (the block itself is left-to-right).

    Hebrew is isolated so the bidi algorithm cannot move quotes and parentheses across it:
    - in JSON lines, the content of each string that contains Hebrew is wrapped in <bdi>;
    - in other lines (diagrams), the Hebrew description from its first Hebrew letter (or an opening quote / parenthesis
      right before it) to the end of the line is wrapped in <bdi dir="rtl">.
    """
    if '":' in line:
        parts: list[str] = []
        last = 0
        for m in JSON_STRING.finditer(line):
            parts.append(html.escape(line[last:m.start()]))
            content = html.escape(m.group(1))
            parts.append(f'"<bdi>{content}</bdi>"' if HEBREW.search(m.group(1)) else f'"{content}"')
            last = m.end()
        parts.append(html.escape(line[last:]))
        return "".join(parts)

    m = HEBREW.search(line)
    if not m:
        return html.escape(line)
    start = m.start()
    while start > 0 and line[start - 1] in "\"'(":
        start -= 1
    return f'{html.escape(line[:start])}<bdi dir="rtl">{html.escape(line[start:])}</bdi>'


def slug(index: int) -> str:
    return f"ch{index}"


class Converter:
    def __init__(self, markdown: str) -> None:
        self.lines = markdown.replace("\r\n", "\n").split("\n")
        self.i = 0
        self.out: list[str] = []
        self.title = "מסמך"
        self.chapters: list[str] = []

    # ---- block parsers ----
    def run(self) -> str:
        cover = self.parse_until_chapter()
        body = "\n".join(self.out)
        toc = "".join(f'<li><a href="#{slug(n + 1)}">{inline(t)}</a></li>' for n, t in enumerate(self.chapters))
        return (
            "<!doctype html>\n"
            '<html lang="he" dir="rtl">\n<head>\n<meta charset="utf-8">\n'
            '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
            f"<title>{html.escape(self.title)}</title>\n"
            '<link rel="preconnect" href="https://fonts.googleapis.com">\n'
            '<link href="https://fonts.googleapis.com/css2?family=Heebo:wght@400;600;700&display=swap" rel="stylesheet">\n'
            f"<style>{CSS}</style>\n</head>\n<body>\n<main>\n"
            f'<section class="cover">\n<div class="radar" aria-hidden="true">◎</div>\n{cover}\n'
            f'<nav class="toc" aria-label="תוכן העניינים"><h2>תוכן העניינים</h2><ol>{toc}</ol></nav>\n</section>\n'
            f"{body}\n</section>\n</main>\n</body>\n</html>\n"
        )

    def parse_until_chapter(self) -> str:
        """The cover: the title (#) and the paragraphs before the first chapter (##)."""
        parts: list[str] = []
        first_para = True
        while self.i < len(self.lines) and not self.lines[self.i].startswith("## "):
            line = self.lines[self.i].strip()
            self.i += 1
            if line.startswith("# "):
                self.title = line[2:].strip()
                parts.append(f"<h1>{inline(self.title)}</h1>")
            elif line:
                cls = "subtitle" if first_para else ""
                first_para = False
                parts.append(f'<p class="{cls}">{inline(line)}</p>' if cls else f"<p>{inline(line)}</p>")
        title, *rest = parts or [""]
        subtitle = [p for p in rest if 'class="subtitle"' in p]
        meta = [p for p in rest if 'class="subtitle"' not in p]
        self.parse_body()
        return title + "".join(subtitle) + f'<div class="meta">{"".join(meta)}</div>'

    def parse_body(self) -> None:
        chapter_open = False
        same_page = False
        while self.i < len(self.lines):
            line = self.lines[self.i]
            stripped = line.strip()
            if not stripped:
                self.i += 1
            elif stripped.startswith("<!--") and stripped.endswith("-->"):
                same_page = "same-page" in stripped
                self.i += 1
            elif line.startswith("## "):
                if chapter_open:
                    self.out.append("</section>")
                text = line[3:].strip()
                self.chapters.append(text)
                n = len(self.chapters)
                cls = "chapter same-page" if same_page else "chapter"
                self.out.append(f'<section class="{cls}" id="{slug(n)}">\n<h2>{inline(text)}</h2>')
                chapter_open = True
                same_page = False
                self.i += 1
            elif line.startswith("### "):
                self.out.append(f"<h3>{inline(line[4:].strip())}</h3>")
                self.i += 1
            elif stripped.startswith("```"):
                self.code_block()
            elif stripped.startswith("|"):
                self.table()
            elif re.match(r"^(- |\d+\. )", stripped):
                self.list_block()
            else:
                self.paragraph()
        if not chapter_open:  # keep the closing tag in run() balanced
            self.out.append("<section>")

    def code_block(self) -> None:
        self.i += 1  # opening fence
        code: list[str] = []
        while self.i < len(self.lines) and not self.lines[self.i].strip().startswith("```"):
            code.append(code_line(self.lines[self.i]))
            self.i += 1
        self.i += 1  # closing fence
        self.out.append(f'<pre dir="ltr"><code>{chr(10).join(code)}</code></pre>')

    def table(self) -> None:
        rows: list[list[str]] = []
        while self.i < len(self.lines) and self.lines[self.i].strip().startswith("|"):
            cells = [c.strip() for c in self.lines[self.i].strip().strip("|").split("|")]
            rows.append(cells)
            self.i += 1
        header, *body = rows
        if body and all(re.fullmatch(r":?-{2,}:?", c) for c in body[0]):
            body = body[1:]
        head = "".join(f'<th scope="col">{inline(c)}</th>' for c in header)
        trs = "".join("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>" for r in body)
        self.out.append(f"<table><thead><tr>{head}</tr></thead><tbody>{trs}</tbody></table>")

    def list_block(self) -> None:
        ordered = bool(re.match(r"^\d+\. ", self.lines[self.i].strip()))
        items: list[str] = []
        while self.i < len(self.lines):
            line = self.lines[self.i]
            m = re.match(r"^(- |\d+\. )(.*)$", line)
            if m:
                items.append(m.group(2).strip())
            elif line.startswith("  ") and line.strip() and items:  # continuation of the previous item
                items[-1] += " " + line.strip()
            else:
                break
            self.i += 1
        tag = "ol" if ordered else "ul"
        self.out.append(f"<{tag}>" + "".join(f"<li>{inline(t)}</li>" for t in items) + f"</{tag}>")

    def paragraph(self) -> None:
        text: list[str] = []
        while self.i < len(self.lines):
            line = self.lines[self.i]
            s = line.strip()
            if not s or line.startswith("#") or s.startswith(("```", "|")) or re.match(r"^(- |\d+\. )", s):
                break
            text.append(s)
            self.i += 1
        self.out.append(f"<p>{inline(' '.join(text))}</p>")


def main(argv: list[str]) -> int:
    src = Path(argv[1]) if len(argv) > 1 else DEFAULT_IN
    dst = Path(argv[2]) if len(argv) > 2 else DEFAULT_OUT
    page = Converter(src.read_text(encoding="utf-8")).run()
    dst.write_text(page, encoding="utf-8", newline="\n")
    print(f"{src} -> {dst} ({len(page):,} chars)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
