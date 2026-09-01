# Архитектура 2.0.1

## Состав решения

- GibddExamSimulator.Core — правила экзамена, выбор вопросов и доменные модели.
- GibddExamSimulator.Application — канонические учебные сессии, профиль ошибок, планы тренировок и координатор синхронизации.
- GibddExamSimulator.Infrastructure — SQLite-хранилище, импорт/проверка банка и Windows updater.
- GibddExamSimulator.Sync — Supabase Auth, REST upload/pull и автоматический вызов Telegram Edge Function.
- GibddExamSimulator.App — WPF-клиент Windows и отдельное окно ExamTerminalWindow.
- GibddExamSimulator.Mobile.Shared — общий Razor UI, состояние и контроллер сессии для Android и PWA.
- GibddExamSimulator.Android — .NET MAUI Blazor Hybrid host, пакетные assets, SQLite и SecureStorage.
- GibddExamSimulator.Web — Blazor WebAssembly host, IndexedDB, service worker и web-адаптеры.

## Поток данных

Каждый клиент создаёт StudySessionEnvelope одинаковой схемы. В нём есть случайный deviceId, тип WindowsDesktop, AndroidApp или MobilePwa, режим, банк/правила, порядок вопросов, ответы, время и итог. Перед записью вычисляется канонический SHA-256.

Завершение сессии сначала одной локальной транзакцией сохраняет запись и outbox. SyncCoordinator отправляет outbox в append-only таблицу Supabase, затем получает страницы чужих устройств по server_seq. Повтор с тем же session_id и тем же payload hash считается успехом; другой hash для того же id считается конфликтом. Курсор меняется только в транзакции применения принятой страницы.

После успешного upload экзамена Sync вызывает telegram-report с пользовательским JWT. Сервер проверяет владельца сессии и доставляет отчёт идемпотентно. Ошибка Telegram оставляет локальный outbox для повтора и не меняет экзаменационный результат.

## Изоляция интерфейсов

Современное главное окно Windows и legacy-терминал намеренно разделены. ExamTerminalWindow не содержит WebView, открывается borderless fullscreen и подключает только Resources/LegacyExamTheme.xaml. Результат и просмотр ошибок используют отдельные WPF views в той же плоской системе.

Android и PWA повторно используют общий Razor UI, но platform adapters различаются: Android читает банк из APK, хранит данные в SQLite и refresh token в SecureStorage; PWA использует IndexedDB и service worker.

## Обновления

Windows читает latest GitHub Release, загружает update-manifest.json либо asset metadata, требует HTTPS и проверяет SHA-256 установщика. Android проверяет latest Release и открывает APK новой версии после явного действия пользователя. PWA получает обновление service worker. Репозиторий настраивается публичным полем gitHubRepository; приватные ключи клиентам не нужны.

## Банк

Единственная копия банка — assets/question-bank/ab. Она включается во все три outputs. Валидатор фиксирует 800 вопросов, 40 билетов, 160 блоков, 548 JPEG, сигнатуры JPEG и SHA-256 JSON. Категории C/D не входят в продукт.
