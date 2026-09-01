#!/usr/bin/env python3
"""Scan packaged outputs, including ZIP/APK entries, for credential-shaped bytes."""

from __future__ import annotations

import argparse
import re
import zipfile
from pathlib import Path


PATTERNS = {
    "Telegram bot token": re.compile(rb"\b\d{8,12}:[A-Za-z0-9_-]{30,}\b"),
    "GitHub token": re.compile(rb"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}\b"),
    "GitHub fine-grained token": re.compile(rb"\bgithub_pat_[A-Za-z0-9_]{20,}\b"),
    "JWT": re.compile(rb"\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\b"),
    "Supabase secret key": re.compile(rb"\bsb_secret_[A-Za-z0-9_-]{16,}\b"),
    "private key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
}


def scan_bytes(value: bytes, source: str, findings: list[str]) -> None:
    for label, pattern in PATTERNS.items():
        if pattern.search(value):
            findings.append(f"{source}: {label}")


def scan_file(path: Path, findings: list[str]) -> None:
    with path.open("rb") as stream:
        tail = b""
        while chunk := stream.read(4 * 1024 * 1024):
            scan_bytes(tail + chunk, str(path), findings)
            tail = chunk[-512:]
    if path.suffix.lower() in {".zip", ".apk"}:
        with zipfile.ZipFile(path) as archive:
            for entry in archive.infolist():
                normalized = entry.filename.replace("\\", "/").lower()
                if normalized.endswith("/.env") or normalized.endswith("/keystore") or normalized.endswith(".jks"):
                    findings.append(f"{path}!{entry.filename}: forbidden secret file")
                if entry.is_dir() or entry.file_size > 128 * 1024 * 1024:
                    continue
                try:
                    scan_bytes(archive.read(entry), f"{path}!{entry.filename}", findings)
                except (OSError, RuntimeError, zipfile.BadZipFile):
                    findings.append(f"{path}!{entry.filename}: could not scan archive entry")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("path", type=Path)
    args = parser.parse_args()
    root = args.path.resolve()
    files = [root] if root.is_file() else sorted(item for item in root.rglob("*") if item.is_file())
    findings: list[str] = []
    for path in files:
        scan_file(path, findings)
    if findings:
        raise SystemExit("Credential-shaped values found in release assets:\n" + "\n".join(sorted(set(findings))))
    print(f"RELEASE_SECRET_SCAN_OK files={len(files)}")


if __name__ == "__main__":
    main()
