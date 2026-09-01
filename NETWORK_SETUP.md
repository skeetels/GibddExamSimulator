# Supabase, Telegram, PWA и обновления 2.0.1

## 1. Сначала перевыпустить Telegram token

Token, ранее отправленный в переписке, считается раскрытым. Отзовите его через BotFather и получите новый. Не помещайте новый token в Git, client-settings.json, APK, EXE, PWA, GitHub Variables, команды с буквальным значением или логи.

## 2. Развернуть Supabase

~~~powershell
supabase login
supabase link --project-ref YOUR_PROJECT_REF
supabase db push
~~~

Создайте длинный случайный webhook secret независимо от bot token, затем задайте оба серверных секрета:

~~~powershell
$env:TELEGRAM_BOT_TOKEN = Read-Host "New Telegram bot token"
$env:TELEGRAM_WEBHOOK_SECRET = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
supabase secrets set TELEGRAM_BOT_TOKEN="$env:TELEGRAM_BOT_TOKEN" TELEGRAM_WEBHOOK_SECRET="$env:TELEGRAM_WEBHOOK_SECRET"
supabase functions deploy telegram-report
supabase functions deploy telegram-bot --no-verify-jwt
~~~

telegram-report сохраняет обязательную проверку пользовательского JWT. telegram-bot отключает Supabase JWT только потому, что webhook вызывается Telegram; endpoint проверяет заголовок X-Telegram-Bot-Api-Secret-Token в constant time.

В Supabase Auth отключите публичную регистрацию. Создайте кандидатов через Dashboard → Authentication → Users. Пароль клиент не сохраняет.

## 3. Зарегистрировать Telegram webhook

Подставьте PROJECT_REF, оставив token и secret только в переменных текущего процесса:

~~~powershell
$webhookBody = @{
  url = "https://PROJECT_REF.supabase.co/functions/v1/telegram-bot"
  secret_token = $env:TELEGRAM_WEBHOOK_SECRET
  allowed_updates = @("message")
  drop_pending_updates = $true
} | ConvertTo-Json
Invoke-RestMethod \
  -Method Post \
  -Uri "https://api.telegram.org/bot$env:TELEGRAM_BOT_TOKEN/setWebhook" \
  -ContentType "application/json" \
  -Body $webhookBody
~~~

Откройте бота из личного аккаунта @skeetels и один раз отправьте /start. Webhook сохранит числовой private chat id в закрытой таблице. Другие usernames, группы и каналы игнорируются. В клиентах нет Chat ID и выбора другого бота.

Доступны команды /stats, /mistakes, /today, /last и /help. После каждого завершённого и синхронизированного экзамена отчёт отправляется автоматически, без кнопки. Временная ошибка Telegram не отменяет экзамен и приводит к retry.

## 4. Подключить Windows, Android и PWA

~~~powershell
python .\tools\configure_deployment.py \
  --supabase-url "https://PROJECT_REF.supabase.co" \
  --supabase-publishable-key "PUBLISHABLE_KEY" \
  --github-repository "OWNER/REPOSITORY" \
  --pages-base "/"
~~~

Скрипт меняет только публичные client settings трёх клиентов. Service role и bot token получает Edge Functions runtime автоматически.

## 5. GitHub Pages

Repository Variables:

~~~text
SUPABASE_URL
SUPABASE_PUBLISHABLE_KEY
PAGES_CUSTOM_DOMAIN
~~~

Settings → Pages → Source: GitHub Actions. pages.yml рассчитывает base path, создаёт SPA fallback и публикует PWA. PWA предназначена для браузера/iPhone и не заменяет Android APK.

## 6. Android production signing

Repository Secrets:

~~~text
ANDROID_KEYSTORE_BASE64
ANDROID_KEY_ALIAS
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_PASSWORD
~~~

Без них release.yml всё равно создаст installable DEV-SIGNED APK с явной пометкой. Для последующих обновлений поверх установленного APK нужен постоянный production keystore; подробности в docs/ANDROID_SIGNING.md.

## 7. Выпуск и обновления

~~~powershell
git tag v2.0.1
git push origin v2.0.1
~~~

release.yml собирает Windows installer, Android ARM64 APK, PWA, source, update-manifest.json и SHA256SUMS.txt. Windows загружает installer только по HTTPS и запускает после совпадения SHA-256. Android предлагает открыть APK asset более новой версии. PWA обновляется через service worker.

После запуска workflow откройте Actions и убедитесь, что все jobs зелёные. Локальный YAML без GitHub run не является подтверждением.
