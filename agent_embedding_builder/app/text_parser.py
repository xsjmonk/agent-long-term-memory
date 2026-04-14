from __future__ import annotations

import re
from pathlib import Path

from app.models import ParsedDocument, ParsedSection


def _looks_like_header(line: str, next_line: str | None) -> bool:
    stripped = line.strip()
    if not stripped or len(stripped) > 80:
        return False
    if stripped.endswith(":"):
        return True
    if stripped[-1:] in ".!?":
        return False
    return next_line is None or next_line.strip() == "" or len(next_line.strip()) > 80


def parse_text(path: Path) -> ParsedDocument:
    raw = path.read_text(encoding="utf-8")
    lines = raw.splitlines()
    paragraphs = [part.strip() for part in re.split(r"\n\s*\n", raw) if part.strip()]
    sections: list[ParsedSection] = []
    current_heading = path.stem
    current_lines: list[str] = []
    current_start = 1
    for index, line in enumerate(lines, start=1):
        next_line = lines[index] if index < len(lines) else None
        if _looks_like_header(line, next_line):
            if current_lines:
                sections.append(
                    ParsedSection(current_heading, 1, "\n".join(current_lines).strip(), [path.stem, current_heading], current_start, index - 1)
                )
                current_lines = []
            current_heading = line.strip().rstrip(":")
            current_start = index + 1
        else:
            current_lines.append(line)
    if current_lines:
        sections.append(
            ParsedSection(current_heading, 1, "\n".join(current_lines).strip(), [path.stem, current_heading], current_start, len(lines))
        )
    if not sections:
        sections = [ParsedSection(path.stem, 1, raw.strip(), [path.stem], 1, max(1, len(lines)))]
    return ParsedDocument(path=path, source_type="text", title=path.stem, raw_text=raw, metadata={}, sections=sections, paragraphs=paragraphs)
