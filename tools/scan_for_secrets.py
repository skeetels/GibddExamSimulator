#!/usr/bin/env python3
"""Fail when source-controlled text contains a credential-shaped production secret."""

from __future__ import annotations

import re
from pathlib import Path


EXCLUDED_DIRECTORIES = {".git", ".vs", "bin", "obj", "artifacts", "outputs", "publish"}
PATTERNS = {
    "Telegram bot token": re.compile(r"\b\d{8,12}:[A-Za-z0-9_-]{30,}\b"),
    "GitHub token": re.compile(r"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}\b"),
    "JWT": re.compile(r"\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\b"),
    "Supabase secret key": re.compile(r"\bsb_secret_[A-Za-z0-9_-]{16,}\b"),
}


def main() -> None:
    repository = Path(__file__).resolve().parents[1]
    findings: list[str] = []
    for path in repository.rglob("*"):
        if not path.is_file() or any(part in EXCLUDED_DIRECTORIES for part in path.parts):
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for name, pattern in PATTERNS.items():
            if pattern.search(text):
                findings.append(f"{path.relative_to(repository)}: {name}")
    if findings:
        raise SystemExit("Secret-shaped values found:\n" + "\n".join(findings))
    print("Secret scan: OK")


if __name__ == "__main__":
    main()
