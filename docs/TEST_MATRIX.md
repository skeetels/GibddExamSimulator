# Матрица проверок 2.0.1

## Автоматические контракты

| Область | Проверка | Ожидаемый результат |
|---|---|---|
| Core/Application | GibddExamSimulator.Tests | правила экзамена, профиль, SQLite, 800/40/548, desktop XAML и Android packaging contract |
| Sync | GibddExamSimulator.Sync.Tests | JWT upload/pull, idempotency, retry Telegram, AndroidApp device marker |
| Mobile | GibddExamSimulator.Web.Tests | экзамен скрывает правильность, training feedback, draft flow |
| Telegram | Deno tests обеих функций | формат отчёта, APK/PWA/ПК marker, команды, только private @skeetels |
| Банк | build_ab_question_bank.py --validate-only | 800 AB, 40 билетов, 160 блоков, 548 настоящих JPEG |
| Secrets | scan_for_secrets.py | нет token/password/private key patterns |
| APK | verify_android_apk.py + aapt2 + apksigner | ZIP, manifest, package/version, assemblies, банк, подпись |
| Database | supabase test db | RLS и append-only ограничения |

Локально 2026-09-01 подтверждено: Core 39/39; Sync 5/5; Web 3/3; Telegram 6/6; WPF Release build 0 warnings; Web Release build 0 warnings; банк OK; secret scan OK. Итоговый Android publish и точные суммы записываются перед упаковкой.

## Windows visual/behavior

Ручная матрица выполняется на 1366×768, 1920×1080 и 2560×1440 при scaling 100%, 125%, 150%. Для каждого режима проверяются overview 20 вопросов, JPEG, вопрос без изображения, выбранный ответ, подтверждённый ответ, supplementary, result и review errors. Обязательны Tahoma, 58/50/42, 5 строк, отдельное подтверждение и отсутствие раскрытия правильности.

Фактический smoke test на Windows 2048×1152/125%: отдельное окно открылось borderless fullscreen; overview показал все 20 карточек в пяти строках; Space открыл вопрос; 1 только выбрала ответ и включила ОТВЕТИТЬ; Right открыл вопрос 2 с пропорциональным JPEG; Esc вернул overview.

## Android manual

- чистая установка Release x64 на API 35 emulator;
- запуск до Home;
- отключение Wi-Fi и data, force-stop/relaunch;
- старт экзамена из 20 вопросов без сети;
- загрузка bundled JPEG;
- подтверждение ответа;
- force-stop/relaunch и восстановление draft на вопросе 3;
- системный Back в активной сессии показывает собственное русское подтверждение; Остаться оставляет экзамен, Выйти возвращает Home с сохранённым draft;
- повторное включение сети и sync;
- проверка отметки Телефон / APK в серверном отчёте после настройки secrets.

## Cross-device после развёртывания

1. Войти одним Supabase user на Android и Windows.
2. Ошибиться на Android, завершить сессию и дождаться sync.
3. На Windows обновить историю: вопрос/билет/блок должны изменить риск и попасть в Работу над ошибками.
4. Завершить ошибочную попытку на Windows и синхронизировать.
5. На Android выполнить resume/manual sync: вопрос должен появиться в общем профиле.
6. Повторить upload одного sessionId: дубликат не создаётся, Telegram не отправляется второй раз.

GitHub Actions нельзя считать запущенными только по наличию YAML. Для финального зелёного run нужен удалённый GitHub repository и настроенные variables/secrets; локальный репозиторий без remote выполняет эквивалентные команды, но не подменяет этот внешний run.
