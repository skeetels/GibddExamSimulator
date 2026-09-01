# GIBDD Exam Simulator 2.0.1

Неофициальный тренажёр теоретического экзамена ГИБДД с общей историей на трёх клиентах:

- Windows WPF — отдельный полноэкранный экзаменационный терминал в компоновке версии 1.2.0;
- Android 8.0+ — настоящий устанавливаемый .NET MAUI Blazor Hybrid APK;
- адаптивная Blazor WebAssembly PWA — браузер, iPhone, Android и ПК.

Проект не является программой МВД России или Госавтоинспекции и не использует их логотипы.

## Что входит в 2.0.1

- ровно 800 вопросов A/B/M: 40 билетов × 20 вопросов, 160 тематических блоков и 548 JPEG;
- отсутствие категорий C/D и WebP в исходном и собранном банке;
- экзамен 20 вопросов / 20 минут с дополнительными 5 или 10 вопросами по ошибкам;
- на Windows сначала виден плоский перечень всех 20 вопросов в пяти строках, затем можно открыть любой вопрос;
- выбор ответа не подтверждает его автоматически, а правильность скрыта до результата;
- умные тренировки учитывают прошлые ошибки, сложные билеты, тематические блоки и время ответа;
- локальная offline-first история, восстановление незавершённой сессии и идемпотентный outbox;
- единая облачная история Windows, Android и PWA через Supabase с RLS;
- автоматический Telegram-отчёт после завершённого экзамена только владельцу @skeetels;
- пометка источника отчёта: ПК, Телефон / APK или Телефон / PWA плюс короткий анонимный код установки;
- обновления Windows с SHA-256, Android через APK последнего GitHub Release и PWA через service worker.

Клиенты не содержат Telegram bot token, service-role key, GitHub PAT, signing password, ФИО владельца компьютера, имя устройства или аппаратный fingerprint.

## Готовые форматы

Windows устанавливается файлом GibddExamSimulator-Setup-2.0.1-win-x64.exe в стандартный каталог:

~~~text
C:\Program Files\GibddExamSimulator
~~~

Локальные данные Windows хранятся отдельно и переживают обновление программы:

~~~text
%LOCALAPPDATA%\GibddExamSimulator\Data\questions.db
%LOCALAPPDATA%\GibddExamSimulator\auth-session.bin
%LOCALAPPDATA%\GibddExamSimulator\Updates\
~~~

Android устанавливается файлом GibddExamSimulator-2.0.1-android-DEV-SIGNED.apk либо production-signed вариантом без суффикса DEV-SIGNED. APK содержит банк и все изображения внутри, поэтому экзамен запускается без GitHub Pages и без интернета. PWA остаётся отдельным web-артефактом и не заменяет APK.

## Настройка сервера

Публичные настройки клиентов создаются одной командой:

~~~powershell
python .\tools\configure_deployment.py \
  --supabase-url "https://PROJECT.supabase.co" \
  --supabase-publishable-key "PUBLISHABLE_KEY" \
  --github-repository "OWNER/REPOSITORY" \
  --pages-base "/"
~~~

Подробная настройка Supabase, Telegram webhook, PWA и обновлений описана в [NETWORK_SETUP.md](NETWORK_SETUP.md). Старый Telegram token необходимо отозвать: [SECURITY_ACTION_REQUIRED.md](SECURITY_ACTION_REQUIRED.md).

## Сборка и тесты

Основные требования: .NET SDK 10.0.203+, Python 3.11+, JDK 17, Android SDK 36; для WPF и Inno Setup нужна Windows x64.

~~~powershell
python .\tools\build_ab_question_bank.py --validate-only
python .\tools\scan_for_secrets.py
dotnet test .\tests\GibddExamSimulator.Tests\GibddExamSimulator.Tests.csproj -c Release
dotnet test .\tests\GibddExamSimulator.Sync.Tests\GibddExamSimulator.Sync.Tests.csproj -c Release
dotnet test .\tests\GibddExamSimulator.Web.Tests\GibddExamSimulator.Web.Tests.csproj -c Release
dotnet build .\src\GibddExamSimulator.App\GibddExamSimulator.App.csproj -c Release
dotnet build .\src\GibddExamSimulator.Web\GibddExamSimulator.Web.csproj -c Release
~~~

Установщик:

~~~powershell
.\installer\Prepare-InnoSetup.ps1
.\installer\Build-Installer.ps1 -AppVersion 2.0.1 -DotnetPath (Get-Command dotnet).Source
~~~

Android APK:

~~~powershell
dotnet workload install maui-android
dotnet restore .\src\GibddExamSimulator.Android\GibddExamSimulator.Android.csproj -r android-arm64
dotnet publish .\src\GibddExamSimulator.Android\GibddExamSimulator.Android.csproj \
  -f net10.0-android -c Release -r android-arm64 \
  -p:AndroidPackageFormats=apk
~~~

Подробности: [архитектура](docs/ARCHITECTURE.md), [сборка Android](docs/ANDROID_BUILD.md), [визуальный контракт Windows](docs/DESKTOP_EXAM_VISUAL_CONTRACT.md), [протокол синхронизации](docs/SYNC_PROTOCOL.md), [матрица тестов](docs/TEST_MATRIX.md), [источник банка](DATA_SOURCES.md).
