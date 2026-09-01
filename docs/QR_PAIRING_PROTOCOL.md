# Протокол одноразовой QR-привязки

## Состояния

Компьютер, уже имеющий anonymous auth и membership, вызывает `POST /pairing/start`. Atomic RPC отменяет предыдущий pending request этого устройства и создаёт новый со статусом `pending`, TTL пять минут и двумя хешами: полного 256-битного секрета и восьмисимвольного short code.

QR содержит только HTTPS invitation:

~~~text
https://PUBLIC_PWA/pair?v=1&id=PAIRING_UUID&secret=URL_SAFE_ONE_TIME_SECRET&env=ENVIRONMENT_ID
~~~

В QR нет JWT/refresh token, GitHub credential, service-role key, bot token, chat ID или постоянного device secret. Полный secret генерируется из 32 random bytes и не логируется. Сервер хранит только lower-case SHA-256.

## Завершение

Телефон до сканирования имеет собственные `auth.uid()`, `deviceId` и временный профиль. Он проверяет HTTPS, protocol version и `environmentId`, затем вызывает `POST /pairing/complete`. RPC блокирует request `FOR UPDATE`, проверяет hash/TTL/status/rate limit, запрещает self-scan, переводит ровно один pending request в `completed` и создаёт membership телефона в профиле компьютера.

Одновременное повторное завершение получает `replayed`; истёкший QR — `expired`; неверный secret — `invalid`. Новый QR делает предыдущий `cancelled`. Короткий код использует тот же request и имеет per-auth sliding window с временной блокировкой перебора.

Компьютер опрашивает `GET /pairing/status` каждые две секунды. Временная потеря сети не останавливает цикл. После `completed` он очищает QR, показывает `Устройства связаны`, запускает push/pull и больше не показывает onboarding при обычном старте.

## Merge и revoke

Перед QR локальные completed sessions остаются в SQLite/IndexedDB. После смены profile scope обе стороны добавляют их через обычный идемпотентный outbox. Draft остаётся локальным. `devices/revoke` ставит `revoked_at` только выбранной membership; RLS немедленно прекращает её доступ, не затрагивая остальные устройства.
