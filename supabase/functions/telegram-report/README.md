# Telegram report Edge Function

The function is called automatically after an authenticated client uploads a
completed exam session. It accepts only a valid Supabase user JWT, reads only
that user's sessions, and sends one idempotent report per session.

The recipient is fixed in source as `@skeetels`. The numeric private chat ID is
learned server-side from a private `/start` message and cached in a table that
client roles cannot read. No client application contains a Telegram bot token or
chat ID.

Before deployment:

1. Revoke the previously exposed BotFather token and issue a new one.
2. Store the replacement only as the Supabase Edge Function secret
   `TELEGRAM_BOT_TOKEN`.
3. Apply both database migrations and deploy `telegram-report` with JWT
   verification enabled.
4. From Telegram account `@skeetels`, open the bot and send `/start` once.

The report contains `ПК` or `Телефон / PWA` and the first six hexadecimal
characters of the anonymous installation ID. It never collects the Windows
computer name, phone model, account password, or other hardware identifiers.
