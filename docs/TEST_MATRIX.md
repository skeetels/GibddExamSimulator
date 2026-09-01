# Матрица проверок 2.0.2

| Область | Автоматическая проверка | Контракт |
|---|---|---|
| Core/Application | `GibddExamSimulator.Tests` | экзаменационные правила, адаптивный профиль, SQLite, 800/40/548, legacy XAML |
| Zero-config UI | `ZeroConfigUiContractTests` | нет login/password/GitHub/Supabase fields; QR, camera, devices, WebCrypto |
| Sync client | `GibddExamSimulator.Sync.Tests` | anonymous signup `{}`, cached offline state, pairing URL, versioned API, retry/idempotency |
| Mobile behavior | `GibddExamSimulator.Web.Tests` | hidden correctness, training feedback, draft lifecycle |
| Device API | Deno fmt/lint/check + tests | 256-bit secret, short code alphabet, safe QR, hash, safe health |
| Telegram | Deno tests | fixed private owner, one-time `/start`, detailed source-marked report |
| Database | `supabase test db` | membership RLS allow/deny, append-only, atomic consume, expiry/replay/self/rate-limit/revoke isolation |
| Question bank | `build_ab_question_bank.py --validate-only` | 800 AB, 40 tickets, 160 blocks, 548 valid JPEG, 0 WebP/C/D |
| Production config | validators + MSBuild target | actual repo/HTTPS/health, same environment/hash in all clients, no placeholders/secrets |
| APK | `verify_android_apk.py`, `aapt2`, `apksigner` | installable package, version 202/2.0.2, assemblies, full offline bank, production signature |
| Release | artifact validator/secret scan | exact mandatory assets, embedded config match, public download and updater hash |

## Windows visual matrix

At 1366×768, 1920×1080 and 2560×1440 with 100/125/150% scaling verify: borderless fullscreen; Tahoma; 58/50/42 px rows; overview of all 20 questions in five rows; proportional JPEG; no-image question; answer selection without automatic confirmation; supplementary block; result; error review; Space/Enter/Esc/Left/Right/1–5. Structural snapshot tests protect critical XAML even when CI screenshot comparison is unreliable.

## Clean-device E2E required before tag

1. Remove Windows local test profile and clean-install signed Windows build.
2. Clean-install signed APK on API 26+ emulator/device.
3. Verify hidden identities, Windows QR and Android camera button.
4. Scan once; both show `Устройства связаны`; restart both without QR.
5. Complete a wrong Android answer, wait automatic push, then start Windows exam and verify updated risk/work-on-errors.
6. Complete a wrong Windows answer and verify it on Android after foreground pull.
7. Disable network, finish a session, restore network and verify outbox delivery without duplicates.
8. Revoke one device and verify only its RLS access disappears.

Six screenshots are required under `docs/evidence/pairing-e2e/`; Release refuses to create `pairing-e2e-evidence.zip` when any is missing.

## Evidence status

Local compiler/unit results are recorded after each change. Supabase hosted health, database deploy, clean emulator pairing, Pages and GitHub workflow URLs must be recorded in `GITHUB_DEPLOYMENT_STATE.md`; local YAML is never reported as a successful remote run.

## Фактический локальный прогон — 2026-09-01

| Проверка | Результат |
|---|---|
| .NET Core/Application | 45/45 passed, Release |
| .NET Sync | 11/11 passed, Release |
| .NET Web behavior | 3/3 passed, Release с `SkipDeploymentConfigValidation=true` только для локальных тестов |
| Deno Edge Functions | 14/14 passed; fmt, lint и type-check для 4 функций |
| Windows/Web/Android | Debug builds: 0 warnings, 0 errors |
| Question bank | 800 вопросов, 40 билетов, 160 блоков, 548 JPEG, 0 WebP/C/D |
| GitHub Actions definitions | 5/5 YAML parsed; `actionlint` passed |
| Windows visual smoke | borderless fullscreen; 20 карточек 4×5; JPEG и no-image; отдельное подтверждение; `Esc` к перечню |
| Android package smoke | ARM64 APK 2.0.2/202; 800/40/548; `apksigner` v2/v3 passed; локально подписан debug certificate |
| Android clean-install smoke | API 35 x86_64 emulator: uninstall 2.0.1, install/start 2.0.2, package/version/ABI verified; smoke-config showed `Открыть камеру` onboarding |
| Production release gate | ожидаемо отклоняет пустой dev config: `environmentId is missing or invalid` |

Локальный APK является только структурной проверкой и не считается production-артефактом. Экран камеры проверялся отдельным временным smoke-config с недоступным endpoint; после проверки тестовый APK был удалён с эмулятора, а source и оставшийся generated APK возвращены к пустому dev contract. Hosted Supabase SQL-тесты, реальная QR-привязка, cross-device sync, Pages, public health и GitHub Release не выполнялись: для них требуются фактический Supabase project, повторно авторизованный GitHub и production signing secrets.
