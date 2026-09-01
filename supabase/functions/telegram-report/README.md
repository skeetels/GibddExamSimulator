# telegram-report

JWT-защищённая Supabase Edge Function автоматически вызывается после upload завершённого экзамена. Она проверяет Supabase user, читает только принадлежащую ему session, получает его историю, формирует подробный отчёт и отправляет его единственному получателю @skeetels.

Фактический private chat id заранее сохраняет telegram-bot после личной команды /start. Клиенты не содержат token/chat id. Таблица telegram_report_deliveries и RPC claim_telegram_report обеспечивают lock, retry и одну доставку на sessionId.

До deployment:

1. отозвать ранее раскрытый BotFather token;
2. сохранить новый как Supabase secret TELEGRAM_BOT_TOKEN;
3. применить миграции;
4. deploy telegram-report с verify_jwt=true;
5. deploy/configure telegram-bot и отправить /start из @skeetels.

Отчёт различает ПК, Телефон / APK и Телефон / PWA, но не собирает имя компьютера, модель телефона или hardware ID. Шесть начальных символов случайного deviceId нужны лишь для различения установок.
