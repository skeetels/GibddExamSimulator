# Deployment owner runbook — не инструкция пользователю

Обычный пользователь не выполняет действия из этого файла. Windows/APK/PWA получают готовые endpoints при сборке и не имеют технической формы настроек.

## GitHub production environment

Создайте environment `production`, включите Pages `GitHub Actions` и задайте repository variables/secrets из `docs/DEPLOYMENT_CONFIG.md`. Ранее раскрытый Telegram token сначала отзовите через BotFather; новый сохраните только как secret.

`backend-deploy.yml` на `main`/tag автоматически:

1. links нужный Supabase project;
2. применяет migrations;
3. устанавливает server-only values;
4. разворачивает `device-api`, `telegram-report`, `telegram-bot`;
5. регистрирует подписанный Telegram webhook;
6. проверяет public `/health`;
7. сохраняет безопасный deployment config artifact.

`pages.yml` встраивает тот же contract и публикует PWA. `release.yml` на `v2.0.4` требует совместимый health, постоянную APK-подпись, production config и шесть E2E screenshots; затем проверяет файлы, создаёт Release и скачивает каждый asset обратно.

## Проверка перед tag

Все jobs `CI`, `Deploy backend` и `Deploy PWA to GitHub Pages` должны быть зелёными. Запишите фактические URLs/run IDs/commit в `docs/GITHUB_DEPLOYMENT_STATE.md`, выполните clean Windows + Android pairing и сохраните evidence. Только затем создавайте tag `v2.0.4`.

Нельзя копировать access token, database password, bot token, webhook secret, keystore или signing password в командную строку, source, Variables, QR, клиентский JSON или отчёт.
