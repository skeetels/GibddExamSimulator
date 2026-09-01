#!/usr/bin/env python3
"""Verify that Windows, APK and PWA payloads embed the same production contract."""

from __future__ import annotations

import argparse
import json
import re
import zipfile
from pathlib import Path

from validate_deployment_config import validate


FORBIDDEN_UI = re.compile(
    r"GitHub owner|GitHub token|Supabase URL|service[_ -]?role|YOUR_|example\.com|localhost|Настроить GitHub",
    re.IGNORECASE,
)


def assert_no_forbidden_ui(raw: bytes, source: str) -> None:
    decoded = raw.decode("utf-8", errors="ignore") + "\n" + raw.decode("utf-16-le", errors="ignore")
    match = FORBIDDEN_UI.search(decoded)
    if match:
        raise ValueError(f"{source}: forbidden runtime setup text found: {match.group(0)!r}")


def read_config(raw: bytes, source: str) -> dict[str, object]:
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise ValueError(f"{source}: deployment config is not an object")
    validate(value)
    if FORBIDDEN_UI.search(raw.decode("utf-8", errors="ignore")):
        raise ValueError(f"{source}: forbidden setup/placeholder text found")
    return value


def config_from_zip(path: Path, suffix: str) -> dict[str, object]:
    with zipfile.ZipFile(path) as archive:
        matches = [name for name in archive.namelist() if name.replace("\\", "/").lower().endswith(suffix)]
        if len(matches) != 1:
            raise ValueError(f"{path}: expected one {suffix}, found {matches}")
        return read_config(archive.read(matches[0]), f"{path}!{matches[0]}")


def scan_client_payload(path: Path, kind: str) -> None:
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            name = entry.filename.replace("\\", "/")
            lowered = name.lower()
            if entry.is_dir() or entry.file_size > 48 * 1024 * 1024:
                continue
            if kind == "pwa":
                selected = (
                    lowered.endswith((".html", ".js", ".css", ".json", ".webmanifest"))
                    or ("/_framework/gibddexamsimulator." in lowered and lowered.endswith(".wasm"))
                )
            else:
                selected = (
                    lowered.startswith("assets/wwwroot/")
                    and lowered.endswith((".html", ".js", ".css", ".json", ".webmanifest"))
                ) or ("/libaot-gibddexamsimulator." in lowered and lowered.endswith(".dll.so"))
            if selected:
                assert_no_forbidden_ui(archive.read(entry), f"{path}!{name}")


def scan_windows_payload(path: Path) -> None:
    candidates = list(path.glob("GibddExamSimulator*.dll")) + list(path.glob("GibddExamSimulator*.exe"))
    if not candidates:
        raise ValueError("Windows validation payload does not contain the built application")
    for candidate in candidates:
        assert_no_forbidden_ui(candidate.read_bytes(), str(candidate))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--deployment-config", type=Path, required=True)
    parser.add_argument("--windows-publish", type=Path, required=True)
    parser.add_argument("--pwa-zip", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--inno-script", type=Path, required=True)
    args = parser.parse_args()

    expected = read_config(args.deployment_config.read_bytes(), str(args.deployment_config))
    windows_config = read_config(
        (args.windows_publish / "Configuration" / "client-settings.json").read_bytes(),
        "Windows publish",
    )
    pwa_config = config_from_zip(args.pwa_zip, "client-settings.json")
    apk_config = config_from_zip(args.apk, "client-settings.json")
    for label, value in (("Windows", windows_config), ("PWA", pwa_config), ("APK", apk_config)):
        if value != expected:
            raise ValueError(f"{label} embeds a different deployment environment")

    scan_windows_payload(args.windows_publish)
    scan_client_payload(args.pwa_zip, "pwa")
    scan_client_payload(args.apk, "apk")

    inno = args.inno_script.read_text(encoding="utf-8")
    if 'Source: "{#PublishDir}\\*"' not in inno or "recursesubdirs" not in inno:
        raise ValueError("Inno Setup no longer packages the complete validated Windows publish directory")
    if FORBIDDEN_UI.search(json.dumps(expected, ensure_ascii=False)):
        raise ValueError("forbidden runtime setup fields found")
    print(
        "PRODUCTION_ARTIFACTS_OK "
        f"environment={expected['environmentId']} configSha256={expected['configSha256']}"
    )


if __name__ == "__main__":
    main()
