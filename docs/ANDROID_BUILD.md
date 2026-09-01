# Android APK 2.0.3

Приложение — настоящий .NET MAUI Android package `app.gibddexamsimulator.mobile`, versionName `2.0.3`, versionCode `203`, minSdk 26. Это не PWA wrapper. В APK находятся 800 вопросов AB и 548 JPEG; WebP и категории C/D запрещены.

## Camera onboarding

`ZXing.Net.Maui.Controls` предоставляет native `CameraBarcodeReaderView`. Permission `android.permission.CAMERA` объявлен в manifest, но runtime-запрос появляется только после нажатия `Открыть камеру`. Отказ показывает русскую инструкцию и допускает повтор. Выбор фотографии вместо живой камеры не используется. Auth хранится через MAUI SecureStorage/Android Keystore.

## Локальная сборка

Нужны .NET 10.0.203+, MAUI Android workload, JDK 17, Android SDK platform 36/build-tools 36.0.0. Сначала должен быть создан настоящий deployment contract; Debug можно собрать без него.

~~~powershell
dotnet restore .\src\GibddExamSimulator.Android\GibddExamSimulator.Android.csproj -r android-arm64
dotnet publish .\src\GibddExamSimulator.Android\GibddExamSimulator.Android.csproj `
  -f net10.0-android -c Release -r android-arm64 --no-restore `
  -p:AndroidPackageFormats=apk
~~~

## Production подпись

Release workflow не имеет dev-signed fallback. Он завершается ошибкой, если отсутствует любой из `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_PASSWORD`. Keystore декодируется только в `RUNNER_TEMP`, не попадает в artifact и должен оставаться одинаковым для будущих обновлений.

Проверки:

~~~powershell
python .\tools\verify_android_apk.py .\GibddExamSimulator-2.0.3-android.apk
apksigner verify --verbose --print-certs .\GibddExamSimulator-2.0.3-android.apk
aapt2 dump badging .\GibddExamSimulator-2.0.3-android.apk
adb install -r --no-incremental .\GibddExamSimulator-2.0.3-android.apk
adb shell monkey -p app.gibddexamsimulator.mobile -c android.intent.category.LAUNCHER 1
~~~

Перед Release нужно провести clean-install E2E: кнопка камеры, QR, `Устройства связаны`, cross-device ошибка, force-stop/relaunch без повторного QR и offline экзамен. Скриншоты входят в `pairing-e2e-evidence.zip`.
