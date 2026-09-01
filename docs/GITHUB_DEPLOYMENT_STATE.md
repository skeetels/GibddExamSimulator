# Фактическое состояние GitHub deployment

Проверено локально 2026-09-01 перед внешним развёртыванием.

| Поле | Фактическое состояние |
|---|---|
| Текущая ветка | `fix/zero-config-qr-sync` |
| Исходный HEAD до исправлений | `1b5e291` (`Release 2.0.1 desktop terminal and Android APK`) |
| Git remote | отсутствует (`git remote -v` не вернул записей) |
| GitHub connection | `UNAUTHORIZED`: подключение требует повторной авторизации |
| Repository owner/name | ещё не определены внешним GitHub |
| Default branch | внешне не определена; workflows ожидают `main` |
| Pages URL | ещё не создан |
| Release URL | ещё не создан |
| Public sync API | ещё не развёрнут |
| Environment ID | ещё не назначен production deployment |
| Успешные workflow IDs/URLs | отсутствуют, поскольку remote отсутствует |
| Tag `v2.0.2` | не создавался до зелёных workflows |
| Supabase deployment access | project reference/database password/access token отсутствуют в окружении |

Это не шаблон с выдуманными адресами: файл намеренно фиксирует проверенный внешний статус. Перед финальным релизом он должен быть заменён фактическими owner/repository, URLs, environment ID, commit SHA/tag и ссылками на последние зелёные runs. Пока таблица содержит состояние `отсутствует`, production-сборки с пустым config обязаны падать и не должны передаваться пользователю.

Необходимые workflow уже versioned в репозитории: `ci.yml`, `backend-deploy.yml`, `pages.yml`, `release.yml`. Их наличие само по себе не считается запуском.

Локально 2026-09-01 подтверждены 59 .NET и 14 Deno тестов, валидность всех пяти workflow-файлов и визуальный smoke полноэкранного Windows-терминала. Эти результаты не подменяют обязательные hosted health, SQL/RLS, Pages, clean-device pairing и public Release проверки.
