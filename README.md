# GIBDD Exam Simulator 2.0.4

Неофициальный тренажёр теоретического экзамена ГИБДД для Windows, Android и браузера. Продукт не является программой МВД России или Госавтоинспекции и не использует государственную символику.

## Обычному пользователю

На первом запуске Windows показывает одноразовый QR-код. На телефоне достаточно нажать `Открыть камеру` и отсканировать его. Регистрация, email, пароль, GitHub, токены, адрес сервера и технические настройки не нужны. После сообщения `Устройства связаны` результаты, ошибки, сложные билеты и тематические блоки переносятся автоматически.

- Windows устанавливается в стандартный `C:\Program Files\GibddExamSimulator`.
- Android — настоящий .NET MAUI APK с камерой, SecureStorage и всеми вопросами внутри.
- PWA работает как дополнительный адаптивный браузерный клиент и защищает auth-сессию IndexedDB через WebCrypto AES-GCM.
- Без интернета экзамены и тренировки сохраняются локально; outbox отправит их после восстановления сети.
- Telegram-отчёт по завершённому экзамену создаётся автоматически и содержит пометку `ПК`, `Телефон / APK` или `Телефон / PWA`.

## Банк и экзамен

В каждой сборке находятся ровно 800 вопросов категории AB: 40 билетов × 20 вопросов, 160 тематических блоков и 548 JPEG-изображений. WebP и категории C/D отсутствуют. Экзамен использует 20 вопросов, 20 минут и официальную логику дополнительных вопросов. Риск-профиль учитывает прошлые ошибки, билеты, тематические блоки и время ответа.

Windows-экзамен открывается отдельным borderless fullscreen-терминалом. Его плоская геометрия, перечень всех 20 вопросов, Tahoma, горячие клавиши и отдельное подтверждение ответа защищены структурными тестами по эталону 1.2.0.

## Архитектура выпуска

Публичные production endpoints генерируются в CI одним versioned-контрактом и встраиваются одинаково в Windows, APK и PWA. Release-сборка с пустым или шаблонным config завершается ошибкой. Пользователь не может редактировать этот контракт из интерфейса.

Секреты Telegram, Supabase service-role, GitHub credentials и Android keystore существуют только на стороне GitHub/Supabase deployment. Ранее раскрытый Telegram token не используется и должен быть отозван согласно [SECURITY_ACTION_REQUIRED.md](SECURITY_ACTION_REQUIRED.md).

Ключевые документы:

- [путь пользователя](docs/ZERO_CONFIG_USER_FLOW.md);
- [QR-протокол](docs/QR_PAIRING_PROTOCOL.md);
- [архитектура](docs/ARCHITECTURE.md);
- [синхронизация](docs/SYNC_PROTOCOL.md);
- [production-конфигурация](docs/DEPLOYMENT_CONFIG.md);
- [Windows visual contract](docs/DESKTOP_EXAM_VISUAL_CONTRACT.md);
- [Android APK](docs/ANDROID_BUILD.md);
- [Telegram](docs/TELEGRAM_BOT.md);
- [матрица проверок](docs/TEST_MATRIX.md).

## Разработка

Нужны .NET SDK 10.0.203+, Python 3.11+, Deno 2.9.6; для Android — JDK 17 и Android SDK 36. Debug-сборка допускает пустой локальный client config и работает offline. Любая Release-сборка клиентов требует предварительно сгенерированный настоящий production contract.

~~~powershell
python .\tools\build_ab_question_bank.py --validate-only
python .\tools\scan_for_secrets.py
dotnet test .\tests\GibddExamSimulator.Tests\GibddExamSimulator.Tests.csproj -c Debug
dotnet test .\tests\GibddExamSimulator.Sync.Tests\GibddExamSimulator.Sync.Tests.csproj -c Debug
dotnet test .\tests\GibddExamSimulator.Web.Tests\GibddExamSimulator.Web.Tests.csproj -c Debug
~~~

Фактические production URLs, commit/tag и зелёные workflow runs фиксируются только после развёртывания в [docs/GITHUB_DEPLOYMENT_STATE.md](docs/GITHUB_DEPLOYMENT_STATE.md); шаблонные значения в этот файл не подставляются.
