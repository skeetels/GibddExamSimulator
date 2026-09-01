#!/usr/bin/env python3
"""Generate one validated public deployment contract for Windows, APK and PWA."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlparse

from validate_deployment_config import validate


FORBIDDEN_MARKERS = ("your_", "your-", "example.", "localhost", "todo", "real_owner")


def https_url(value: str, name: str) -> str:
    normalized = value.strip().rstrip("/")
    parsed = urlparse(normalized)
    if (
        parsed.scheme != "https"
        or not parsed.netloc
        or not parsed.hostname
        or parsed.username
        or parsed.password
        or parsed.query
        or parsed.fragment
    ):
        raise ValueError(f"{name} must be an absolute HTTPS URL")
    return normalized


def reject_placeholders(value: str, name: str) -> None:
    lowered = value.lower()
    if not value.strip() or any(marker in lowered for marker in FORBIDDEN_MARKERS):
        raise ValueError(f"{name} is empty or contains a placeholder")


def canonical_hash(value: dict[str, object]) -> str:
    payload = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest().upper()


def write_json(path: Path, value: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    repository_root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--environment-id", required=True)
    parser.add_argument("--supabase-url", required=True)
    parser.add_argument("--supabase-publishable-key", required=True)
    parser.add_argument("--sync-api-base-url", required=True)
    parser.add_argument("--github-repository", required=True)
    parser.add_argument("--pages-url", required=True)
    parser.add_argument("--release-manifest-url", default="")
    parser.add_argument("--telegram-bot-username", required=True)
    parser.add_argument("--pages-base", default="/")
    parser.add_argument("--output", default="build/generated/deployment-config.json")
    args = parser.parse_args()

    environment_id = args.environment_id.strip()
    reject_placeholders(environment_id, "environmentId")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,79}", environment_id):
        raise ValueError("environmentId contains unsupported characters")

    repository_parts = args.github_repository.strip().split("/")
    if len(repository_parts) != 2 or not all(re.fullmatch(r"[A-Za-z0-9_.-]+", item) for item in repository_parts):
        raise ValueError("--github-repository must have OWNER/REPOSITORY format")
    owner, repository_name = repository_parts
    reject_placeholders(owner, "repositoryOwner")
    reject_placeholders(repository_name, "repositoryName")
    repository_url = f"https://github.com/{owner}/{repository_name}"

    supabase_url = https_url(args.supabase_url, "supabaseUrl")
    sync_api = https_url(args.sync_api_base_url, "syncApiBaseUrl")
    pages_url = https_url(args.pages_url, "pagesBaseUrl") + "/"
    release_manifest = args.release_manifest_url.strip() or (
        f"{repository_url}/releases/latest/download/update-manifest.json"
    )
    release_manifest = https_url(release_manifest, "releaseManifestUrl")

    publishable_key = args.supabase_publishable_key.strip()
    reject_placeholders(publishable_key, "supabasePublishableKey")
    if publishable_key.lower().startswith("sb_secret_") or "service_role" in publishable_key.lower():
        raise ValueError("A secret/service-role key cannot be written to client configuration")

    telegram_username = args.telegram_bot_username.strip().lstrip("@")
    reject_placeholders(telegram_username, "telegramBotUsername")
    if not re.fullmatch(r"[A-Za-z0-9_]{5,32}", telegram_username):
        raise ValueError("telegramBotUsername is invalid")

    base = args.pages_base.strip()
    if not base.startswith("/") or not base.endswith("/") or ".." in base:
        raise ValueError("--pages-base must start and end with '/' and cannot contain '..'")

    config: dict[str, object] = {
        "configVersion": 1,
        "environmentId": environment_id,
        "repositoryOwner": owner,
        "repositoryName": repository_name,
        "repositoryUrl": repository_url,
        "releaseManifestUrl": release_manifest,
        "pagesBaseUrl": pages_url,
        "syncApiBaseUrl": sync_api,
        "supabaseUrl": supabase_url,
        "supabasePublishableKey": publishable_key,
        "telegramBotUsername": telegram_username,
    }
    config["configSha256"] = canonical_hash(config)
    validate(config)

    targets = [
        repository_root / "src/GibddExamSimulator.App/Configuration/client-settings.json",
        repository_root / "src/GibddExamSimulator.Web/wwwroot/client-settings.json",
        repository_root / "src/GibddExamSimulator.Android/Configuration/client-settings.json",
        repository_root / args.output,
    ]
    for target in targets:
        write_json(target, config)

    index_path = repository_root / "src/GibddExamSimulator.Web/wwwroot/index.html"
    index = index_path.read_text(encoding="utf-8")
    index = re.sub(r'<base href="[^"]*"\s*/>', f'<base href="{base}" />', index, count=1)
    index_path.write_text(index, encoding="utf-8", newline="\n")

    worker_path = repository_root / "src/GibddExamSimulator.Web/wwwroot/service-worker.published.js"
    worker = worker_path.read_text(encoding="utf-8")
    worker = re.sub(r'const base = "[^"]*";', f'const base = "{base}";', worker, count=1)
    worker_path.write_text(worker, encoding="utf-8", newline="\n")

    print(f"deployment config generated: {config['configSha256']}")


if __name__ == "__main__":
    main()
