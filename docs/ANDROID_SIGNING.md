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

## Dev-signed fallback

Если все четыре secrets не настроены, workflow всё равно выпускает installable APK с development key и обязательным именем GibddExamSimulator-2.0.1-android-DEV-SIGNED.apk. Он пригоден для ручной установки и проверки, но будущая production-сборка с другим ключом не обновит его поверх: dev-вариант придётся удалить вместе с локальными данными.

После сборки всегда выполняется apksigner verify --verbose. Пароль, keystore, Bot token, service-role key и GitHub PAT не должны попадать в APK.
