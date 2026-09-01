# Сборка и проверка Android APK

## Требования

- .NET SDK 10.0.203 или совместимый 10.0.x;
- workload maui-android;
- JDK 17;
- Android SDK platform 36 и build-tools 36.0.0;
- для smoke test — adb и Android emulator/device API 26+.

~~~powershell
dotnet workload install maui-android
sdkmanager "platforms;android-36" "build-tools;36.0.0" "platform-tools"
~~~

## Release APK для современных телефонов

Из корня репозитория:

~~~powershell
dotnet restore .\src\GibddExamSimulator.Android\GibddExamSimulator.Android.csproj -r android-arm64
dotnet publish .\src\GibddExamSimulator.Android\GibddExamSimulator.Android.csproj \
  -f net10.0-android \
  -c Release \
  -r android-arm64 \
  --no-restore \
  -p:AndroidPackageFormats=apk
~~~

Исходный файл появляется в:

~~~text
src\GibddExamSimulator.Android\bin\Release\net10.0-android\android-arm64\publish\app.gibddexamsimulator.mobile-Signed.apk
~~~

Dev-signed результат выпуска переименовывается в GibddExamSimulator-2.0.1-android-DEV-SIGNED.apk. Production-вариант с постоянным keystore — в GibddExamSimulator-2.0.1-android.apk.

Для эмулятора x86_64 замените runtime на android-x64.

## Проверка

~~~powershell
python .\tools\verify_android_apk.py .\path\to\app-Signed.apk
$env:JAVA_HOME = "C:\path\to\jdk-17"
& "$env:ANDROID_HOME\build-tools\36.0.0\apksigner.bat" verify --verbose .\path\to\app-Signed.apk
& "$env:ANDROID_HOME\build-tools\36.0.0\aapt2.exe" dump badging .\path\to\app-Signed.apk
adb install -r --no-incremental .\path\to\app-Signed.apk
adb shell monkey -p app.gibddexamsimulator.mobile -c android.intent.category.LAUNCHER 1
adb shell dumpsys activity activities | Select-String app.gibddexamsimulator.mobile
~~~

verify_android_apk.py проверяет читаемость ZIP, AndroidManifest.xml, managed assemblies, 800 вопросов AB, 40 билетов, 548 JPEG и отсутствие WebP/C/D.

## Фактический smoke test 2026-09-01

На API 35 Google APIs x86_64 Release APK был установлен через adb install --no-incremental. Package app.gibddexamsimulator.mobile с versionName 2.0.1/versionCode 201 запустился до главного экрана. После отключения Wi-Fi и mobile data приложение было принудительно остановлено и запущено снова: банк открылся, экзамен из 20 вопросов стартовал, JPEG загрузился из APK. Системный Back показал русское модальное подтверждение; Остаться сохранило экран, Выйти вернуло на Home с карточкой восстановления. Повторный force-stop/relaunch ранее восстановил draft на вопросе 3.

Финальный ARM64 dev-signed APK имеет размер 55 271 086 байт и SHA-256 294B4B467DCE7B47EC15327D3828FFAE671DE3E234D726A377BCA17A11F1F664. aapt2 подтвердил native-code arm64-v8a, minSdk 26 и targetSdk 36; apksigner подтвердил схемы v2 и v3. APK содержит 800 вопросов, 40 билетов, 548 JPEG и 0 WebP.

## APK, PWA и AAB

- APK — устанавливаемый файл Android; именно он выдаётся пользователю.
- AAB — пакет публикации магазина, напрямую обычно не устанавливается и не заменяет APK.
- PWA — web-клиент браузера/service worker; полезен для iPhone и браузера, но это не Android-приложение.

Типичные ошибки: MAUI Debug с fast deployment нельзя раздавать как standalone APK; для adb нужен Signed.apk из Release publish. JDK должен быть версии 17, ANDROID_HOME — указывать на SDK, а runtime эмулятора должен совпадать с android-x64.
