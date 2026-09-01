# Telegram-отчёты и команды

Telegram реализован только на сервере Supabase. Ни Windows, ни APK, ни PWA не содержат token или chat_id и не обращаются к Bot API напрямую.

## Компоненты

- telegram-report — JWT-защищённая функция. Принимает sessionId, повторно проверяет пользователя и принадлежность сессии, формирует отчёт и использует delivery ledger/lock.
- telegram-bot — webhook для Bot API. JWT выключен только для этого endpoint; каждый запрос обязан иметь Telegram secret header.
- telegram-delivery-worker — подписанный server-only retry worker для durable pending/failed очереди.
- telegram_private_recipients — закрытая таблица связи фиксированного recipient key с фактическим личным chat id.
- telegram_profile_links/telegram_link_tokens — одноразовый deep-link конкретного learning profile.
- telegram_report_deliveries — идемпотентность, retry и статус отправки.

## Получатель

Код принимает только private-сообщения Telegram-аккаунта с username skeetels, без учёта регистра. Кнопка `Подключить Telegram` получает десятиминутный token и открывает `https://t.me/<fixed-bot>?start=<token>`. `/start` атомарно потребляет token, связывает профиль с реальным chat id и сохраняет fixed-recipient fallback. Другие usernames, группы и каналы возвращают ignored и никогда не становятся получателями. Пользователь не вводит chat ID и не выбирает бота.

## Автоматический отчёт

После каждого успешно загруженного экзамена database trigger создаёт durable delivery, а API вызывает telegram-report; кнопки `Отправить` нет. Сообщение содержит итог, дату Екатеринбурга, источник ПК/Телефон APK/Телефон PWA, короткий случайный код установки, общее время, длительности ответов, ошибки текущей попытки, сложные билеты и тематические блоки. Аппаратные имена и учётные данные не включаются.

Если Telegram временно недоступен, экзамен и основная синхронизация остаются успешными, delivery получает `failed`, а server-only scheduled worker повторяет очередь. Один sessionId доставляется не более одного раза.

## Команды владельца

- /start — безопасно связать личный чат и показать справку;
- /stats — число экзаменов, сдано/не сдано и общая точность;
- /mistakes — сложные билеты и тематические блоки;
- /today — подсказка текущей тренировки и публичные ссылки;
- /last — последний экзаменационный отчёт;
- /help — список команд.

`backend-deploy.yml` устанавливает server secrets, разворачивает функции и регистрирует подписанный webhook через `tools/configure_telegram_webhook.py`. До развёртывания обязательно отозвать ранее раскрытый token и сохранить новый только как GitHub/Supabase secret `TELEGRAM_BOT_TOKEN`.
