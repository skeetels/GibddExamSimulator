#!/usr/bin/env python3
"""Validate the immutable public production configuration embedded in every client."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import urllib.request
from pathlib import Path
from urllib.parse import urlparse


EXPECTED_FIELDS = {
    "configVersion",
    "environmentId",
    "repositoryOwner",
    "repositoryName",
    "repositoryUrl",
    "releaseManifestUrl",
    "pagesBaseUrl",
    "syncApiBaseUrl",
    "supabaseUrl",
    "supabasePublishableKey",
    "telegramBotUsername",
    "configSha256",
}
EXPECTED_API_VERSION = "1"
EXPECTED_CLIENT_VERSION = "2.0.2"
EXPECTED_BANK_VERSION = "ab-2025-05-26"
FORBIDDEN_MARKERS = (
    "localhost",
    "127.0.0.1",
    "example.com",
    "example.test",
    "your_",
    "your-",
    "real_owner",
    "todo",
    "change_me",
)
SECRET_PATTERNS = (
    re.compile(r"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}\b"),
    re.compile(r"\bgithub_pat_[A-Za-z0-9_]{20,}\b"),
    re.compile(r"\bsb_secret_[A-Za-z0-9_-]{16,}\b"),
    re.compile(r"\b\d{8,12}:[A-Za-z0-9_-]{30,}\b"),
)


def canonical_hash(value: dict[str, object]) -> str:
    public_value = {key: item for key, item in value.items() if key != "configSha256"}
    payload = json.dumps(public_value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest().upper()


def require_https(value: object, field: str) -> str:
    text = str(value or "").strip()
    parsed = urlparse(text)
    if (
        parsed.scheme != "https"
        or not parsed.netloc
        or not parsed.hostname
        or parsed.username
        or parsed.password
        or parsed.query
        or parsed.fragment
    ):
        raise ValueError(f"{field} must be an absolute public HTTPS URL")
    return text


def validate(value: dict[str, object]) -> None:
    if set(value) != EXPECTED_FIELDS:
        missing = sorted(EXPECTED_FIELDS - set(value))
        extra = sorted(set(value) - EXPECTED_FIELDS)
        raise ValueError(f"deployment config fields differ; missing={missing}, extra={extra}")
    if value["configVersion"] != 1:
        raise ValueError("unsupported deployment config version")

    serialized = json.dumps(value, ensure_ascii=False)
    lowered = serialized.lower()
    marker = next((item for item in FORBIDDEN_MARKERS if item in lowered), None)
    if marker:
        raise ValueError(f"deployment config contains forbidden placeholder: {marker}")
    if any(pattern.search(serialized) for pattern in SECRET_PATTERNS):
        raise ValueError("deployment config contains a credential-shaped secret")
    if "service_role" in lowered:
        raise ValueError("deployment config contains a service-role key")

    environment = str(value["environmentId"] or "")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,79}", environment):
        raise ValueError("environmentId is missing or invalid")
    owner = str(value["repositoryOwner"] or "")
    repository = str(value["repositoryName"] or "")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+", owner) or not re.fullmatch(r"[A-Za-z0-9_.-]+", repository):
        raise ValueError("repository owner/name is missing or invalid")

    repository_url = require_https(value["repositoryUrl"], "repositoryUrl").rstrip("/")
    expected_repository_url = f"https://github.com/{owner}/{repository}"
    if repository_url.lower() != expected_repository_url.lower():
        raise ValueError("repositoryUrl does not match repositoryOwner/repositoryName")
    release_url = require_https(value["releaseManifestUrl"], "releaseManifestUrl")
    pages_url = require_https(value["pagesBaseUrl"], "pagesBaseUrl")
    sync_url = require_https(value["syncApiBaseUrl"], "syncApiBaseUrl")
    supabase_url = require_https(value["supabaseUrl"], "supabaseUrl").rstrip("/")
    release_parts = urlparse(release_url)
    expected_release_prefix = f"/{owner}/{repository}/releases/".lower()
    if release_parts.hostname.lower() != "github.com" or not release_parts.path.lower().startswith(expected_release_prefix):
        raise ValueError("releaseManifestUrl belongs to a different repository")
    supabase_host = urlparse(supabase_url).hostname or ""
    sync_host = urlparse(sync_url).hostname or ""
    if not supabase_host.endswith(".supabase.co"):
        raise ValueError("supabaseUrl must identify the deployed Supabase project")
    if sync_host != supabase_host:
        raise ValueError("syncApiBaseUrl must belong to the configured Supabase project")
    if not pages_url.endswith("/"):
        raise ValueError("pagesBaseUrl must end with '/'")
    if not sync_url.rstrip("/").endswith("/device-api"):
        raise ValueError("syncApiBaseUrl must identify the versioned device-api function")

    publishable_key = str(value["supabasePublishableKey"] or "")
    if len(publishable_key) < 20:
        raise ValueError("Supabase publishable key is missing")
    if not re.fullmatch(r"[A-Za-z0-9_]+", str(value["telegramBotUsername"] or "")):
        raise ValueError("Telegram public username is missing or invalid")
    expected_hash = canonical_hash(value)
    if str(value["configSha256"]).upper() != expected_hash:
        raise ValueError("configSha256 does not match the canonical public contract")


def load(path: Path) -> tuple[dict[str, object], bytes]:
    raw = path.read_bytes()
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise ValueError(f"{path} does not contain a JSON object")
    validate(value)
    return value, raw


def check_health(config: dict[str, object]) -> None:
    url = str(config["syncApiBaseUrl"]).rstrip("/") + "/health"
    request = urllib.request.Request(
        url,
        headers={"X-Environment-Id": str(config["environmentId"]), "User-Agent": "gibdd-release-validator/2.0.3"},
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        payload = json.load(response)
    if response.status != 200 or payload.get("status") != "ok":
        raise ValueError("sync health-check did not return status=ok")
    if payload.get("environmentId") != config["environmentId"]:
        raise ValueError("sync health-check returned another environmentId")
    if payload.get("apiVersion") != EXPECTED_API_VERSION:
        raise ValueError("sync health-check returned an incompatible apiVersion")
    if payload.get("minimumClientVersion") != EXPECTED_CLIENT_VERSION:
        raise ValueError("sync health-check returned an incompatible minimumClientVersion")
    if payload.get("bankVersion") != EXPECTED_BANK_VERSION:
        raise ValueError("sync health-check returned a different bankVersion")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--compare-clients", action="store_true")
    parser.add_argument("--health", action="store_true")
    args = parser.parse_args()

    config_path = args.config if args.config.is_absolute() else root / args.config
    config, raw = load(config_path.resolve())
    if args.compare_clients:
        clients = (
            root / "src/GibddExamSimulator.App/Configuration/client-settings.json",
            root / "src/GibddExamSimulator.Android/Configuration/client-settings.json",
            root / "src/GibddExamSimulator.Web/wwwroot/client-settings.json",
        )
        for client in clients:
            _, client_raw = load(client)
            if client_raw != raw:
                raise ValueError(f"client configuration differs from {config_path}: {client}")
    if args.health:
        check_health(config)
    print(f"DEPLOYMENT_CONFIG_OK environment={config['environmentId']} sha256={config['configSha256']}")


if __name__ == "__main__":
    main()
