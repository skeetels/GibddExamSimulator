# Билеты ГИБДД AB — версия 2.0.0

Готовый монорепозиторий двух клиентов одного учебного профиля:

- полноэкранное Windows-приложение WPF для экзамена;
- адаптивная Blazor WebAssembly PWA для телефона, планшета и ПК.

Это неофициальный учебный тренажёр. Он не является программой МВД России или Госавтоинспекции и не использует их логотипы.

## Возможности

- ровно 800 вопросов A/B/M: 40 билетов × 20 вопросов, 160 тематических блоков и 548 настоящих JPEG;
- строгая проверка состава и SHA-256 банка при запуске и в CI;
- экзамен 20 вопросов / 20 минут, дополнительные 5 или 10 вопросов и официально-подобные правила ошибок;
- отдельное подтверждение ответа, неизменность подтверждённого ответа и монотонный таймер;
- все 20 номеров вопросов одновременно видны на экране и доступны в произвольном порядке;
- Windows-клиент открывается в borderless fullscreen и адаптируется к текущему разрешению, без фиксации 4:3;
- мобильные режимы: экзамен, «Умные 10», работа над ошибками, слабые темы, билет, марафон и «Без ошибок»;
- анализ прошлых ошибок и медленных ответов по вопросам, билетам и тематическим блокам;
- локальное offline-first хранение: SQLite на Windows и IndexedDB в PWA;
- append-only синхронизация завершённых сессий через Supabase с RLS, курсором и идемпотентным outbox;
- черновик активной мобильной сессии и ленивый кэш изображений;
- полный офлайн-пакет изображений с оценкой свободного места, прогрессом, отменой и очисткой;
- автоматический Telegram-отчёт после каждого завершённого экзамена только владельцу `@skeetels`;
- источник отчёта отмечается как `ПК · A1B2C3` либо `Телефон / PWA · A1B2C3`, где код — первые шесть символов случайного ID установки;
- обновления Windows через GitHub Releases с обязательной проверкой SHA-256 и подтверждением перед запуском установщика;
- обновление PWA через service worker и ненавязчивую плашку «Доступна новая версия».

В облако и Telegram не передаются ФИО, имя компьютера, модель телефона, Windows username, пароли или аппаратный fingerprint. Клиенты не содержат Telegram token и не обращаются к Bot API напрямую.

## Установка Windows

Запустите `GibddExamSimulator-Setup-2.0.0-win-x64.exe`. Стандартный путь:

```text
C:\Program Files\GibddExamSimulator
```

Локальная история и защищённая сессия хранятся отдельно от программы:

```text
%LOCALAPPDATA%\GibddExamSimulator\Data\questions.db
%LOCALAPPDATA%\GibddExamSimulator\auth-session.bin
%LOCALAPPDATA%\GibddExamSimulator\Updates\
```

Установщик требует права администратора для `Program Files`. Ярлык рабочего стола предлагается как необязательная задача. Коммерческой подписи кода нет, поэтому SmartScreen может показать предупреждение неизвестного издателя; сверяйте SHA-256 из выпуска.

## Первый запуск владельца

1. Разверните Supabase и серверную Telegram-функцию по [NETWORK_SETUP.md](NETWORK_SETUP.md).
2. Создайте кандидату пользователя в Supabase Auth; самостоятельная регистрация отключена.
3. Сгенерируйте публичную конфигурацию клиентов:

```powershell
python .\tools\configure_deployment.py \`
  --supabase-url "https://PROJECT.supabase.co" \`
  --supabase-publishable-key "PUBLISHABLE_KEY" \`
  --github-repository "OWNER/REPOSITORY" \`
  --pages-base "/"
```

В конфигурации допустимы только URL проекта, publishable key и публичное имя GitHub-репозитория. Без Supabase оба клиента работают локально; облачная история и Telegram начнут работать после настройки.

## Сборка

Требуются .NET SDK 10.0.203+, Python 3.11+ и Windows x64 для WPF/установщика.

```powershell
dotnet restore .\GibddExamSimulator.sln
python .\tools\build_ab_question_bank.py --validate-only
python .\tools\scan_for_secrets.py
dotnet test .\GibddExamSimulator.sln -c Release
.\installer\Prepare-InnoSetup.ps1
.\installer\Build-Installer.ps1 -AppVersion 2.0.0 -DotnetPath (Get-Command dotnet).Source
```

PWA:

```powershell
dotnet publish .\src\GibddExamSimulator.Web\GibddExamSimulator.Web.csproj \`
  -c Release -o .\artifacts\publish\pwa
```

`ci.yml` проверяет банк, секреты, C#, WPF и Edge Function. `pages.yml` публикует PWA из `main`; `release.yml` по тегу `v*` создаёт Windows installer, `update-manifest.json`, checksum и PWA ZIP.

Подробнее о происхождении банка: [DATA_SOURCES.md](DATA_SOURCES.md). Обязательное действие с ранее раскрытым токеном: [SECURITY_ACTION_REQUIRED.md](SECURITY_ACTION_REQUIRED.md).
