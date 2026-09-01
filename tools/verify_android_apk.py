#!/usr/bin/env python3
"""Performs deterministic, dependency-free checks on a packaged Android APK."""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
import zipfile


EXPECTED_JPEG_COUNT = 548


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("apk", type=pathlib.Path)
    args = parser.parse_args()
    apk = args.apk.resolve()
    if not apk.is_file() or apk.stat().st_size == 0:
        raise SystemExit(f"APK is missing or empty: {apk}")

    with zipfile.ZipFile(apk) as archive:
        bad = archive.testzip()
        if bad is not None:
            raise SystemExit(f"Corrupt APK entry: {bad}")
        names = archive.namelist()
        lower_names = [name.lower() for name in names]
        bank_assets = [name for name in lower_names if "question-bank/ab/" in name]
        question_entries = [name for name in bank_assets if name.endswith("questions.json")]
        if len(question_entries) != 1:
            raise SystemExit("Bundled questions.json is missing or ambiguous")
        original_question_name = names[lower_names.index(question_entries[0])]
        payload = json.loads(archive.read(original_question_name))

    if "androidmanifest.xml" not in lower_names:
        raise SystemExit("AndroidManifest.xml is missing")
    if not any(
        "libassemblies." in name and name.endswith(".blob.so")
        or name.endswith("gibddexamsimulator.android.dll.so")
        or name.endswith("gibddexamsimulator.android.dll")
        for name in lower_names
    ):
        raise SystemExit("Managed application assemblies are missing")

    jpegs = [name for name in bank_assets if name.endswith(".jpg") or name.endswith(".jpeg")]
    webp = [name for name in bank_assets if name.endswith(".webp")]
    if len(jpegs) != EXPECTED_JPEG_COUNT:
        raise SystemExit(
            f"Expected {EXPECTED_JPEG_COUNT} bundled AB JPEGs, found {len(jpegs)}"
        )
    if webp:
        raise SystemExit(f"Question bank contains WebP files: {webp[:3]}")
    questions = payload.get("questions", payload) if isinstance(payload, dict) else payload
    if not isinstance(questions, list) or len(questions) != 800:
        raise SystemExit(f"Expected 800 AB questions, found {len(questions) if isinstance(questions, list) else 'invalid JSON'}")
    categories = {str(item.get("category", "")).upper() for item in questions}
    if categories != {"AB"}:
        raise SystemExit(f"Unexpected categories in APK bank: {sorted(categories)}")
    tickets = {int(item.get("ticketNumber", item.get("ticket"))) for item in questions}
    if len(tickets) != 40:
        raise SystemExit(f"Expected 40 tickets, found {len(tickets)}")

    print(
        f"APK_OK path={apk} bytes={apk.stat().st_size} "
        f"entries={len(names)} questions={len(questions)} tickets={len(tickets)} "
        f"ab_jpegs={len(jpegs)} webp=0"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
