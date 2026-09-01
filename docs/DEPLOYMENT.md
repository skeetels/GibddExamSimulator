# Развёртывание 2.0.4

Этот документ адресован владельцу deployment. Пользователь программы ничего не разворачивает и не вводит.

- `ci.yml` проверяет банк, secrets, clients, Edge Functions, RLS/pairing SQL, Windows visual contract и installable Android smoke APK.
- `backend-deploy.yml` применяет migrations, server secrets, функции, Telegram webhook и health.
- `pages.yml` публикует camera-enabled offline PWA с тем же immutable config.
- `release.yml` выпускает строго `v2.0.4`, требует production APK signature и E2E evidence, проверяет embedded endpoints и публичное скачивание.

Поля/секреты перечислены в `DEPLOYMENT_CONFIG.md`; текущий внешний статус — в `GITHUB_DEPLOYMENT_STATE.md`; порядок — в корневом `NETWORK_SETUP.md`.
