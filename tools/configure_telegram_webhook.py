#!/usr/bin/env python3
"""Configure the fixed Telegram bot webhook without printing credentials."""

from __future__ import annotations

import json
import os
import urllib.parse
import urllib.request


def required(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise SystemExit(f"Missing required environment value: {name}")
    return value


def main() -> None:
    token = required("TELEGRAM_BOT_TOKEN")
    webhook_url = required("TELEGRAM_WEBHOOK_URL")
    webhook_secret = required("TELEGRAM_WEBHOOK_SECRET")
    payload = urllib.parse.urlencode(
        {
            "url": webhook_url,
            "secret_token": webhook_secret,
            "allowed_updates": json.dumps(["message"]),
            "drop_pending_updates": "false",
        }
    ).encode("utf-8")
    request = urllib.request.Request(
        f"https://api.telegram.org/bot{token}/setWebhook",
        data=payload,
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        result = json.load(response)
    if response.status != 200 or not result.get("ok"):
        raise SystemExit("Telegram rejected the webhook configuration")
    print("TELEGRAM_WEBHOOK_OK")


if __name__ == "__main__":
    main()
