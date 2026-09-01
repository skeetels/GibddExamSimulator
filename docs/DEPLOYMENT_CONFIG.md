# Единый production deployment contract

## Публичные поля

Versioned schema находится в `deployment/deployment-config.schema.json`. Generated JSON содержит environment ID, фактический GitHub owner/repository, repository/release/Pages/API/Supabase HTTPS URLs, publishable key, публичное имя Telegram-бота и канонический hash. Один и тот же JSON встраивается в Windows, APK и PWA; runtime-редактора нет.

~~~text
python tools/configure_deployment.py \
  --environment-id production \
  --supabase-url https://PROJECT.supabase.co \
  --supabase-publishable-key PUBLIC_KEY \
  --sync-api-base-url https://PROJECT.supabase.co/functions/v1/device-api \
  --github-repository OWNER/REPOSITORY \
  --pages-url https://OWNER.github.io/REPOSITORY \
  --telegram-bot-username PUBLIC_BOT_USERNAME \
  --pages-base /REPOSITORY/
~~~

Эта команда предназначена для CI/deployment владельца, не для друга или кандидата. Release MSBuild targets вызывают `validate_deployment_config.py`; без настоящего contract сборка падает.

## GitHub Variables

~~~text
DEPLOYMENT_ENVIRONMENT_ID
SUPABASE_URL
SUPABASE_PUBLISHABLE_KEY
SYNC_API_BASE_URL
TELEGRAM_BOT_USERNAME
PAGES_URL
PAGES_CUSTOM_DOMAIN (необязательно)
~~~

## GitHub Secrets

~~~text
SUPABASE_ACCESS_TOKEN
SUPABASE_PROJECT_REF
SUPABASE_DB_PASSWORD
TELEGRAM_BOT_TOKEN
TELEGRAM_WEBHOOK_SECRET
TELEGRAM_DELIVERY_WORKER_SECRET
ANDROID_KEYSTORE_BASE64
ANDROID_KEY_ALIAS
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_PASSWORD
~~~

Secrets никогда не передаются `configure_deployment.py`. Backend workflow устанавливает их только в Supabase/GitHub runtime.

## Проверки выпуска

`validate_deployment_config.py --compare-clients --health` проверяет HTTPS, repo consistency, отсутствие placeholders/secrets, environment и API health. `validate_production_artifacts.py` открывает PWA ZIP и APK и сравнивает их config с Windows publish. `scan_release_assets.py` ищет credential patterns в готовых файлах и ZIP/APK entries.
