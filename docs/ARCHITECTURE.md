# Архитектура 2.0.4

## Компоненты

- `Core` — правила экзамена, модель вопроса и адаптивный выбор.
- `Application` — неизменяемая учебная сессия, профиль ошибок, outbox, sync/device contracts.
- `Infrastructure` — SQLite, миграция legacy-истории, DPAPI и Windows updater.
- `Sync` — скрытая anonymous Auth, versioned `device-api` и push/pull.
- `App` — современная домашняя страница Windows и изолированный fullscreen legacy-терминал.
- `Mobile.Shared` — общий адаптивный Razor UI и учебная логика Android/PWA.
- `Android` — нативный .NET MAUI host, камера ZXing, SQLite и Android SecureStorage/Keystore.
- `Web` — Blazor WebAssembly PWA, IndexedDB, WebCrypto, камера `getUserMedia` и service worker.
- `supabase` — Postgres/RLS, atomic pairing RPC, Edge API и Telegram workers.

## Скрытая идентификация

Каждая установка создаёт случайный `deviceId`, не связанный с железом. При доступной сети клиент без UI вызывает anonymous Supabase signup и получает отдельный `auth.uid()`. Refresh token хранится через DPAPI на Windows, SecureStorage/Keystore на Android и AES-256-GCM с non-exportable WebCrypto key в PWA.

`learning_profile` принадлежит не login-аккаунту, а группе активных `device_memberships`. Новая anonymous identity сначала имеет собственный профиль. Одноразовый QR атомарно переносит её membership в профиль компьютера и объединяет завершённые локальные сессии. Email/password в новом UI отсутствуют.

## Поток данных

Завершение экзамена/тренировки одной локальной транзакцией сохраняет `StudySessionEnvelope` и outbox, затем удаляет draft. Сессия append-only, имеет UUID и SHA-256 канонического payload. API проверяет membership, вставляет запись идемпотентно и назначает `server_seq`. Pull использует курсор и атомарно применяет страницу вместе с новой revision.

Windows, Android и PWA пересчитывают учебный профиль из общей истории. Поэтому ошибка на телефоне влияет на подбор следующего ПК-экзамена, а ошибка Windows появляется в мобильной `Работе над ошибками`.

## Границы безопасности

Клиент получает только HTTPS URLs и Supabase publishable key. Pairing secret живёт пять минут, передаётся только в QR и в базе хранится как SHA-256. Service-role, bot token, webhook secret, signing key и GitHub write credential находятся только в deployment secrets. RLS запрещает чтение чужого `profile_id`; direct client UPDATE/DELETE завершённых сессий отсутствуют.

Telegram не является транспортом синхронизации. После server-side insert экзамена delivery ledger инициирует отдельный идемпотентный отчёт только фиксированному владельцу.

## Интерфейс Windows

Главная страница 2.x содержит QR, прогресс, устройства, Telegram и обновления. Сам экзамен запускается в отдельном `ExamTerminalWindow`, который подключает только `LegacyExamTheme.xaml`; современные карточки и скругления туда не попадают. Эталон и матрица разрешений описаны в `DESKTOP_EXAM_VISUAL_CONTRACT.md`.

## Deployment

`tools/configure_deployment.py` создаёт один канонический public contract и копирует его в три клиента. SHA-256 считается без поля `configSha256`. Release targets вызывают `validate_deployment_config.py`; пустые значения, placeholder, другой repo, HTTP URL или credential-shaped content останавливают сборку. Backend, Pages и Release разнесены по отдельным workflows.
