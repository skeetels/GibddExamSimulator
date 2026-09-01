# telegram-bot

Webhook-команды Telegram для фиксированного владельца @skeetels. Supabase JWT для endpoint отключён, потому что запрос приходит от Telegram; каждый POST проверяется по секретному заголовку TELEGRAM_WEBHOOK_SECRET. Принимаются только private messages нужного username.

/start сохраняет фактический chat id в telegram_private_recipients. Остальные команды работают только если chat id и username совпадают с сохранёнными:

- /stats
- /mistakes
- /today
- /last
- /help

Секреты TELEGRAM_BOT_TOKEN и TELEGRAM_WEBHOOK_SECRET задаются только через supabase secrets set. Инструкция setWebhook находится в корневом NETWORK_SETUP.md.
