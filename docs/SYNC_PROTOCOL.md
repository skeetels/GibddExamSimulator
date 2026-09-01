# Протокол синхронизации Windows, Android и PWA

## Каноническая сессия

`StudySessionEnvelope` schemaVersion 1 содержит `sessionId`, `deviceId`, `deviceKind`, mode, UTC start/finish, bank/rules versions, порядок вопросов, ответы, correctness, длительности и итог. `PayloadSha256` вычисляется из детерминированного canonical JSON; completed session не редактируется.

## Локальная атомарность

Windows/Android используют SQLite, PWA — одну IndexedDB transaction. Завершение сначала сохраняет immutable session и outbox, затем удаляет draft. Сбой процесса оставляет либо обе записи, либо ни одной. Draft не отправляется на сервер.

## API v1

Клиенты обращаются только к заранее встроенному `device-api`:

- `POST /identity/bootstrap`;
- `POST /sync/push`;
- `GET /sync/pull?after=REVISION&limit=N`;
- pairing/device/Telegram endpoints.

JWT определяет anonymous device membership. Service-role остаётся внутри Edge Function. Push сверяет membership по `deviceId`, банк и payload hashes; повтор того же `sessionId`/hash возвращает `AlreadyExists`, другой hash — `IntegrityConflict`. Pull возвращает только общий `profile_id`, строго после cursor, по возрастанию `server_seq`.

## Merge

При первом hidden-auth локальный scope `deviceId` мигрирует в scope `auth.uid()` с проверкой content hash. После QR server profile меняется, но локальные сессии не удаляются: обе стороны повторно отправляют append-only историю. Одинаковые UUID/hash дедуплицируются, разные UUID объединяются, агрегированный профиль пересчитывается из всех завершённых сессий.

## Триггеры

Push выполняется после завершения, запуска приложения, восстановления сети и retry scheduler. Pull — при запуске, перед новым ПК-экзаменом, после pairing, после push, при resume и каждые две минуты в foreground mobile UI. Предэкзаменационный pull ограничен пятью секундами, после чего экзамен безопасно использует локальную историю.

## Offline и retry

Сеть не нужна для уже установленного банка и локального экзамена. Due outbox использует exponential backoff с jitter; повтор не создаёт дубль. Пользователь видит только понятный статус `Офлайн — отправим позже` либо `Синхронизировано`, без endpoint-настроек. Курсор обновляется в одной транзакции с принятой страницей.

## Telegram

Server insert экзамена создаёт durable delivery row и вызывает `telegram-report`. Telegram failure не изменяет study session. Delivery lock и `sessionId` делают отчёт идемпотентным. Основной перенос прогресса всегда идёт через Postgres/API, а не через Telegram или GitHub.
