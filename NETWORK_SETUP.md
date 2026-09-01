# Supabase, Telegram, PWA и обновления

## 1. Сначала перевыпустить Telegram token

Token, ранее отправленный в переписке, считается раскрытым. До публикации или передачи сборки отзовите его через BotFather и получите новый. Новый token нельзя помещать в исходники, клиентские JSON, установщик, PWA, GitHub Variables или логи.

## 2. Развернуть Supabase

Создайте проект Supabase и установите Supabase CLI. Из корня репозитория выполните:

```powershell
supabase login
supabase link --project-ref YOUR_PROJECT_REF
supabase db push
supabase secrets set TELEGRAM_BOT_TOKEN="NEW_ROTATED_TOKEN"
supabase functions deploy telegram-report
```

Edge Function получает `SUPABASE_URL`, publishable/anon key и service-role key из среды Supabase. Вручную переносить service-role key в приложение не нужно и нельзя.

В Auth отключите публичную регистрацию. Для друга создайте пользователя через Dashboard → Authentication → Users → Add user и передайте ему только email и временный пароль. Пароль клиент не сохраняет; refresh token Windows защищается DPAPI.

Миграции создают append-only `study_sessions`, включают и принудительно применяют RLS, разрешают роли `authenticated` только `SELECT` и `INSERT` своих строк и запрещают клиентские `UPDATE`/`DELETE`. Таблицы Telegram и функция выдачи lock доступны только `service_role`.

## 3. Подключить клиентов

Возьмите Project URL и publishable key в настройках API проекта. Это публичные идентификаторы клиента, но не пользовательские учётные данные.

```powershell
python .\tools\configure_deployment.py \`
  --supabase-url "https://PROJECT.supabase.co" \`
  --supabase-publishable-key "PUBLISHABLE_KEY" \`
  --github-repository "OWNER/REPOSITORY" \`
  --pages-base "/"
```

Скрипт обновляет оба `client-settings.json`. Примеры без реальных значений находятся рядом с ними как `client-settings.example.json`.

## 4. Telegram только для @skeetels

Получатель жёстко задан сервером как `@skeetels`; поля Chat ID и выбора бота в клиентах отсутствуют. После развёртывания нового token:

1. Откройте нового бота из личного аккаунта `@skeetels`.
2. Отправьте `/start` до первого экзаменационного отчёта.
3. Если у этого бота ранее был webhook, отключите его, чтобы Edge Function могла получить личный `/start` через `getUpdates`.

При первой отправке функция находит внутренний числовой ID только личного чата с username `skeetels`, сохраняет его в закрытой серверной таблице и больше не ищет получателя. Другие пользователи, группы и каналы игнорируются.

После завершения экзамена клиент сначала атомарно сохраняет сессию локально, затем синхронизирует её и автоматически вызывает Edge Function. Отдельной кнопки отправки нет. Отчёт содержит:

- результат, дату и длительность;
- пометку `ПК · XXXXXX` или `Телефон / PWA · XXXXXX`;
- ошибки текущего экзамена и время каждого ответа;
- самые проблемные билеты и тематические блоки по общей истории;
- долю ошибок и среднее время.

Одна сессия отправляется не более одного раза: серверный delivery ledger и lock делают операцию идемпотентной. Ошибка сети, отсутствие `/start` или временная занятость не удаляют outbox; отправка повторяется при следующей синхронизации.

## 5. GitHub Pages для телефона

В публичном GitHub-репозитории задайте Repository Variables:

```text
SUPABASE_URL
SUPABASE_PUBLISHABLE_KEY
PAGES_CUSTOM_DOMAIN        # необязательно
```

Settings → Pages → Source установите `GitHub Actions`. Workflow `pages.yml` сам рассчитывает base path вида `/REPOSITORY_NAME/`, создаёт SPA fallback и публикует production PWA. После первого открытия установите её через меню браузера «На экран Домой» / «Установить приложение».

## 6. Windows-обновления

Поле `gitHubRepository` в desktop `client-settings.json` должно содержать `OWNER/REPOSITORY`. Приложение при запуске читает только публичный latest release.

Для выпуска новой версии:

```powershell
git tag v2.0.1
git push origin v2.0.1
```

`release.yml` на Windows:

- запускает валидацию и тесты;
- собирает self-contained installer;
- вычисляет SHA-256;
- создаёт совместимый `update-manifest.json`;
- прикладывает installer, checksum и PWA ZIP к GitHub Release встроенным `GITHUB_TOKEN`.

На компьютере кандидата новая версия показывается как предложение. Установка начинается только после подтверждения, только по HTTPS и только после совпадения SHA-256. GitHub PAT в приложении не используется.
