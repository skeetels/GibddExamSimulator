#!/usr/bin/env python3
"""Recover the fixed owner from a previously received Telegram update."""

from __future__ import annotations

import copy
import json
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request


def required(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise SystemExit(f"Missing required environment value: {name}")
    return value


def optional(name: str) -> str:
    return os.environ.get(name, "").strip()


def telegram_call(token: str, method: str, payload: dict[str, object]) -> object:
    for attempt in range(5):
        request = urllib.request.Request(
            f"https://api.telegram.org/bot{token}/{method}",
            data=urllib.parse.urlencode(payload).encode("utf-8"),
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                result = json.load(response)
            if response.status != 200 or not result.get("ok"):
                raise RuntimeError(f"Telegram rejected {method}")
            return result.get("result")
        except urllib.error.HTTPError as error:
            try:
                failure = json.load(error)
            except (json.JSONDecodeError, OSError, ValueError):
                failure = {}
            retry_after = int(failure.get("parameters", {}).get("retry_after", 0))
            if error.code == 429 and attempt < 4:
                time.sleep(max(1, min(30, retry_after or 2 ** attempt)))
                continue
            raise RuntimeError(f"Telegram rejected {method} with HTTP {error.code}") from None
    raise RuntimeError(f"Telegram rejected {method}")


def post_json(url: str, headers: dict[str, str], payload: dict[str, object]) -> dict[str, object]:
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json", **headers},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=90) as response:
        result = json.load(response)
    if response.status != 200:
        raise RuntimeError(f"Server rejected recovery with HTTP {response.status}")
    return result


def main() -> None:
    token = required("TELEGRAM_BOT_TOKEN")
    webhook_url = required("TELEGRAM_WEBHOOK_URL")
    webhook_secret = required("TELEGRAM_WEBHOOK_SECRET")
    worker_url = required("TELEGRAM_DELIVERY_WORKER_URL")
    worker_secret = required("TELEGRAM_DELIVERY_WORKER_SECRET")
    owner = required("TELEGRAM_OWNER_USERNAME").removeprefix("@").casefold()
    owner_chat_id = optional("TELEGRAM_OWNER_CHAT_ID")
    if owner_chat_id and not re.fullmatch(r"-?[0-9]{1,20}", owner_chat_id):
        raise RuntimeError("Stored owner chat id is invalid")

    webhook_removed = not owner_chat_id
    if webhook_removed:
        telegram_call(token, "deleteWebhook", {"drop_pending_updates": "false"})
    try:
        updates: list[dict[str, object]] = []
        if owner_chat_id:
            numeric_chat_id = int(owner_chat_id)
            replay: dict[str, object] = {
                "update_id": 0,
                "message": {
                    "message_id": 0,
                    "date": int(time.time()),
                    "chat": {"id": numeric_chat_id, "type": "private"},
                    "from": {
                        "id": numeric_chat_id,
                        "is_bot": False,
                        "username": owner,
                    },
                    "text": "/start",
                },
            }
        else:
            for _ in range(5):
                result = telegram_call(
                    token,
                    "getUpdates",
                    {
                        "limit": 100,
                        "timeout": 0,
                        "allowed_updates": json.dumps(["message"]),
                    },
                )
                updates = result if isinstance(result, list) else []
                if updates:
                    break
                time.sleep(1)

            owner_updates = []
            for update in updates:
                message = update.get("message")
                if not isinstance(message, dict):
                    continue
                chat = message.get("chat")
                sender = message.get("from")
                if not isinstance(chat, dict) or not isinstance(sender, dict):
                    continue
                username = str(sender.get("username", "")).casefold()
                if chat.get("type") == "private" and username == owner:
                    owner_updates.append(update)
            if not owner_updates:
                raise RuntimeError("No previously received private owner update is available")

            latest = max(owner_updates, key=lambda item: int(item.get("update_id", 0)))
            replay = copy.deepcopy(latest)
            replay_message = replay.get("message")
            if not isinstance(replay_message, dict):
                raise RuntimeError("Owner update has no message")
            replay_message["text"] = "/start"
        accepted = post_json(
            webhook_url,
            {"x-telegram-bot-api-secret-token": webhook_secret},
            replay,
        )
        if accepted.get("accepted") is not True or accepted.get("linked") is not True:
            raise RuntimeError("Owner recovery was not accepted")

        if updates:
            highest_update_id = max(int(item.get("update_id", 0)) for item in updates)
            telegram_call(
                token,
                "getUpdates",
                {"offset": highest_update_id + 1, "limit": 1, "timeout": 0},
            )

        delivery = post_json(
            worker_url,
            {"x-telegram-worker-secret": worker_secret},
            {},
        )
        if delivery.get("ok") is not True:
            raise RuntimeError("Telegram delivery worker rejected recovery")
        print(
            "TELEGRAM_OWNER_RECOVERED "
            f"sent={int(delivery.get('sent', 0))} pending={int(delivery.get('pending', 0))}"
        )
    finally:
        if webhook_removed:
            telegram_call(
                token,
                "setWebhook",
                {
                    "url": webhook_url,
                    "secret_token": webhook_secret,
                    "allowed_updates": json.dumps(["message"]),
                    "drop_pending_updates": "false",
                },
            )


if __name__ == "__main__":
    main()
