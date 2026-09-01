#!/usr/bin/env python3
"""Generate public client configuration and a GitHub Pages-compatible base path."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


def write_json(path: Path, value: dict[str, str]) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    repository = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--supabase-url", default="")
    parser.add_argument("--supabase-publishable-key", default="")
    parser.add_argument("--github-repository", default="")
    parser.add_argument("--pages-base", default="/")
    args = parser.parse_args()

    base = args.pages_base.strip()
    if not base.startswith("/") or not base.endswith("/") or ".." in base:
        raise ValueError("--pages-base must start and end with '/' and cannot contain '..'.")

    write_json(
        repository / "src/GibddExamSimulator.App/Configuration/client-settings.json",
        {
            "supabaseUrl": args.supabase_url.strip(),
            "supabasePublishableKey": args.supabase_publishable_key.strip(),
            "gitHubRepository": args.github_repository.strip(),
        },
    )
    write_json(
        repository / "src/GibddExamSimulator.Web/wwwroot/client-settings.json",
        {
            "supabaseUrl": args.supabase_url.strip(),
            "supabasePublishableKey": args.supabase_publishable_key.strip(),
            "gitHubRepository": args.github_repository.strip(),
        },
    )
    write_json(
        repository / "src/GibddExamSimulator.Android/Configuration/client-settings.json",
        {
            "supabaseUrl": args.supabase_url.strip(),
            "supabasePublishableKey": args.supabase_publishable_key.strip(),
            "gitHubRepository": args.github_repository.strip(),
        },
    )

    index_path = repository / "src/GibddExamSimulator.Web/wwwroot/index.html"
    index = index_path.read_text(encoding="utf-8")
    index = re.sub(r'<base href="[^"]*"\s*/>', f'<base href="{base}" />', index, count=1)
    index_path.write_text(index, encoding="utf-8", newline="\n")

    worker_path = repository / "src/GibddExamSimulator.Web/wwwroot/service-worker.published.js"
    worker = worker_path.read_text(encoding="utf-8")
    worker = re.sub(r'const base = "[^"]*";', f'const base = "{base}";', worker, count=1)
    worker_path.write_text(worker, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
