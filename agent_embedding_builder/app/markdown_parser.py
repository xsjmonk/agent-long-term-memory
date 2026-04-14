from __future__ import annotations

import re
from pathlib import Path

import yaml

from app.models import ParsedDocument, ParsedSection

HEADING_RE = re.compile(r"^(#{1,6})\s+(.*)$")


def _extract_front_matter(text: str) -> tuple[dict, str]:
    if not text.startswith("---\n"):
        return {}, text
    parts = text.split("\n---\n", 1)
    if len(parts) != 2:
        return {}, text
    metadata = yaml.safe_load(parts[0].removeprefix("---\n")) or {}
    return metadata, parts[1]


def parse_markdown(path: Path) -> ParsedDocument:
    raw = path.read_text(encoding="utf-8")
    metadata, body = _extract_front_matter(raw)
    lines = body.splitlines()
    sections: list[ParsedSection] = []
    title = metadata.get("title") or path.stem

    current_heading = title
    current_level = 1
    current_path = [title]
    current_lines: list[str] = []
    start_line = 1
    stack: list[tuple[int, str]] = [(0, title)]

    def flush(end_line: int) -> None:
        nonlocal current_lines, start_line, current_heading, current_level, current_path
        text = "\n".join(current_lines).strip()
        if text:
            sections.append(
                ParsedSection(
                    heading=current_heading,
                    level=current_level,
                    body=text,
                    heading_path=list(current_path),
                    start_line=start_line,
                    end_line=end_line,
                )
            )
        current_lines = []

    for index, line in enumerate(lines, start=1):
        match = HEADING_RE.match(line)
        if match:
            flush(index - 1)
            hashes, heading = match.groups()
            level = len(hashes)
            while stack and stack[-1][0] >= level:
                stack.pop()
            stack.append((level, heading.strip()))
            current_heading = heading.strip()
            current_level = level
            current_path = [item[1] for item in stack]
            start_line = index
        else:
            current_lines.append(line)
    flush(len(lines))

    paragraphs = [part.strip() for part in re.split(r"\n\s*\n", body) if part.strip()]
    return ParsedDocument(
        path=path,
        source_type="markdown",
        title=title,
        raw_text=body,
        metadata=metadata,
        sections=sections,
        paragraphs=paragraphs,
    )
