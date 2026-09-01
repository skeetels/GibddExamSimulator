# Подпись Android

ApplicationId зафиксирован как app.gibddexamsimulator.mobile. Для обновления поверх установленной версии все production-выпуски должны использовать один и тот же защищённый keystore и тот же alias.

## Production в GitHub Actions

В Repository Secrets задаются:

~~~text
ANDROID_KEYSTORE_BASE64
ANDROID_KEY_ALIAS
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_PASSWORD
~~~

release.yml декодирует keystore только во временный RUNNER_TEMP, передаёт параметры AndroidSigningKeyStore/Alias/StorePass/KeyPass в dotnet publish и не загружает keystore как artifact. GitHub маскирует значения secrets, но пароли всё равно нельзя печатать или добавлять в Variables.

Пример подготовки base64 выполняется вне репозитория:

~~~powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\gibdd-release.keystore")) |
  Set-Clipboard
~~~

Храните исходный keystore и пароли в резервной защищённой копии. Потеря ключа лишит возможности устанавливать обновления поверх production APK.

## Политика Release 2.0.3

Dev-signed APK допустим только как CI smoke artifact и всегда имеет суффикс `DEV-SIGNED`. Публичный Release 2.0.3 без всех четырёх signing secrets завершается ошибкой; asset `GibddExamSimulator-2.0.3-android.apk` всегда подписан постоянным production keystore.

После сборки всегда выполняется apksigner verify --verbose. Пароль, keystore, Bot token, service-role key и GitHub PAT не должны попадать в APK.
