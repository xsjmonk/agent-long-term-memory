from __future__ import annotations

import json
from dataclasses import dataclass, field


PATH_FIELDS = {
    ("knowledge_base", "input_roots", "*"),
    ("llm", "local_model_path"),
    ("llm", "download_source", "local_dir"),
    ("embedding", "local_model_path"),
    ("category_governance", "ontology_path"),
    ("category_governance", "schema_path"),
}


@dataclass
class _Context:
    kind: str
    path: tuple[str, ...]
    pending_key: str | None = None
    expecting_value: bool = False
    array_index: int = 0


def preprocess_operator_jsonc(raw_text: str) -> str:
    output: list[str] = []
    stack: list[_Context] = []
    i = 0
    while i < len(raw_text):
        ch = raw_text[i]
        nxt = raw_text[i + 1] if i + 1 < len(raw_text) else ""
        if ch == "/" and nxt == "/":
            end = raw_text.find("\n", i)
            if end == -1:
                output.append(raw_text[i:])
                break
            output.append(raw_text[i:end])
            i = end
            continue
        if ch == "/" and nxt == "*":
            end = raw_text.find("*/", i + 2)
            if end == -1:
                output.append(raw_text[i:])
                break
            output.append(raw_text[i : end + 2])
            i = end + 2
            continue
        if ch == '"':
            value_path = _current_value_path(stack)
            if _is_path_field(value_path):
                end_idx, raw_value = _read_path_string(raw_text, i)
                output.append(json.dumps(_normalize_operator_path(raw_value)))
                i = end_idx + 1
                _finalize_scalar_value(stack)
                continue
            end_idx = _read_standard_string_end(raw_text, i)
            token = raw_text[i : end_idx + 1]
            output.append(token)
            next_sig = _peek_next_significant(raw_text, end_idx + 1)
            if next_sig == ":":
                key = json.loads(token)
                if stack and stack[-1].kind == "object":
                    stack[-1].pending_key = key
                    stack[-1].expecting_value = True
            else:
                _finalize_scalar_value(stack)
            i = end_idx + 1
            continue
        output.append(ch)
        if ch == "{":
            path = _consume_value_path(stack)
            stack.append(_Context(kind="object", path=path))
        elif ch == "[":
            path = _consume_value_path(stack)
            stack.append(_Context(kind="array", path=path))
        elif ch == "}":
            if stack and stack[-1].kind == "object":
                stack.pop()
            _finalize_scalar_value(stack)
        elif ch == "]":
            if stack and stack[-1].kind == "array":
                stack.pop()
            _finalize_scalar_value(stack)
        elif ch == ",":
            if stack:
                if stack[-1].kind == "object":
                    stack[-1].pending_key = None
                    stack[-1].expecting_value = False
                elif stack[-1].kind == "array":
                    stack[-1].array_index += 1
        i += 1
    return "".join(output)


def _normalize_operator_path(value: str) -> str:
    normalized = value.replace("\\", "/")
    if normalized in {"", "/"}:
        return normalized
    if len(normalized) == 3 and normalized[1:] == ":/":
        return normalized
    return normalized.rstrip("/")


def _is_path_field(path: tuple[str, ...] | None) -> bool:
    if path is None:
        return False
    return path in PATH_FIELDS


def _current_value_path(stack: list[_Context]) -> tuple[str, ...] | None:
    if not stack:
        return None
    current = stack[-1]
    if current.kind == "object" and current.expecting_value and current.pending_key is not None:
        return (*current.path, current.pending_key)
    if current.kind == "array":
        return (*current.path, "*")
    return None


def _consume_value_path(stack: list[_Context]) -> tuple[str, ...]:
    path = _current_value_path(stack) or ()
    if stack and stack[-1].kind == "object":
        stack[-1].expecting_value = False
    return path


def _finalize_scalar_value(stack: list[_Context]) -> None:
    if stack and stack[-1].kind == "object":
        stack[-1].expecting_value = False


def _read_standard_string_end(text: str, start: int) -> int:
    escaped = False
    i = start + 1
    while i < len(text):
        ch = text[i]
        if escaped:
            escaped = False
        elif ch == "\\":
            escaped = True
        elif ch == '"':
            return i
        i += 1
    raise ValueError("Unterminated string in operator config")


def _read_path_string(text: str, start: int) -> tuple[int, str]:
    chars: list[str] = []
    i = start + 1
    while i < len(text):
        ch = text[i]
        if ch == '"' and _looks_like_string_end(text, i + 1):
            return i, "".join(chars)
        chars.append(ch)
        i += 1
    raise ValueError("Unterminated path string in operator config")


def _looks_like_string_end(text: str, idx: int) -> bool:
    nxt = _peek_next_significant(text, idx)
    return nxt in {",", "}", "]", ":"}


def _peek_next_significant(text: str, idx: int) -> str | None:
    i = idx
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if ch.isspace():
            i += 1
            continue
        if ch == "/" and nxt == "/":
            end = text.find("\n", i)
            if end == -1:
                return None
            i = end + 1
            continue
        if ch == "/" and nxt == "*":
            end = text.find("*/", i + 2)
            if end == -1:
                return None
            i = end + 2
            continue
        return ch
    return None
