# Обязательные действия владельца до публикации

Ранее Telegram bot token был передан в переписке и поэтому считается скомпрометированным. Версия 2.0.2 не содержит его, но отзыв старого значения обязателен.

1. В BotFather отзовите старый token и выпустите новый.
2. Сохраните новый только как Supabase secret TELEGRAM_BOT_TOKEN.
3. Создайте независимый случайный TELEGRAM_WEBHOOK_SECRET и зарегистрируйте webhook по NETWORK_SETUP.md.
4. Не публикуйте исходные архивы старых версий, где мог присутствовать token.
5. Включите GitHub Secret Scanning и Push Protection.

Client settings могут содержать только versioned environment ID, Supabase URL/publishable key, public GitHub/Pages/Release/API metadata, Telegram public username и config hash. В клиентские файлы запрещены service-role key, bot token, chat id, GitHub PAT, Android keystore и signing passwords.

Telegram Edge Functions принимают только фиксированный private username @skeetels, получают фактический chat id через подписанный webhook /start, проверяют пользовательский JWT для отчёта и используют идемпотентный delivery ledger. Ошибка Telegram не меняет локальный результат.

Для Android production updates создайте постоянный keystore и храните его только в защищённой резервной копии и GitHub Secrets. Release без signing secrets теперь намеренно падает; DEV-SIGNED допустим только как непубличный CI smoke artifact.

Перед выпуском выполните:

~~~powershell
python .\tools\scan_for_secrets.py
python .\tools\build_ab_question_bank.py --validate-only
python .\tools\scan_release_assets.py .\outputs
~~~

Также проверьте готовые EXE/APK/ZIP, SHA256SUMS.txt и логи Actions. Не выводите старый или новый token в отчётах.
