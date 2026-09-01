# Развёртывание 2.0.1

## Публичная конфигурация

Перед сборкой запустите tools/configure_deployment.py с Supabase URL, publishable key, OWNER/REPOSITORY и base path Pages. Скрипт обновляет client-settings.json Windows, Android и PWA. Эти значения публичны; service-role и Telegram token туда не добавляются.

## Supabase

Примените миграции, отключите публичную регистрацию, создайте пользователей через Dashboard, задайте TELEGRAM_BOT_TOKEN и TELEGRAM_WEBHOOK_SECRET, разверните telegram-report и telegram-bot. Затем зарегистрируйте webhook и отправьте /start из @skeetels. Полные команды — в NETWORK_SETUP.md.

## GitHub

Repository Variables:

~~~text
SUPABASE_URL
SUPABASE_PUBLISHABLE_KEY
PAGES_CUSTOM_DOMAIN
~~~

Repository Secrets для production Android:

~~~text
ANDROID_KEYSTORE_BASE64
ANDROID_KEY_ALIAS
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_PASSWORD
~~~

ci.yml разделяет Linux tests, Edge Functions, RLS, Windows WPF и Android ARM64 APK. pages.yml сохраняет публикацию PWA. release.yml на tag v* собирает installer, APK, PWA и source, создаёт update-manifest.json и SHA256SUMS.txt, передаёт промежуточные файлы через actions/upload-artifact и создаёт GitHub Release.

## Выпуск

~~~powershell
git tag v2.0.1
git push origin v2.0.1
~~~

Перед передачей пользователям убедитесь, что Actions зелёные, Release содержит обязательные пять файлов, APK подписан ожидаемым сертификатом, а SHA256SUMS.txt соответствует загруженным assets. Windows installer обновляет прежний AppId в Program Files; пользовательская SQLite база остаётся в LocalAppData.
