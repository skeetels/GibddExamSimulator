# Android smoke screenshots

Снимки получены 2026-09-01 на Android API 35 Google APIs x86_64 из installable Release APK при отключённой сети.

- 01-home.png — главный экран, метка Телефон / APK и 548/548 bundled JPEG.
- 02-offline-session.png — локально созданный экзамен 20 вопросов без сети.
- 03-image-question.png — JPEG из APK с сохранением пропорций.
- 04-back-confirmation.png — русское подтверждение выхода с сохранением draft.
- 05-saved-draft.png — возврат на Home и доступная кнопка восстановления.

Исходный APK прошёл verify_android_apk.py, aapt2 и apksigner v2/v3; package app.gibddexamsimulator.mobile, versionName 2.0.1, versionCode 201.
