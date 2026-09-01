# Pairing E2E evidence

Release requires real screenshots from a clean production-configured Windows install and a clean signed Android APK:

1. `01-windows-qr.png` — first-run QR and short code;
2. `02-android-camera.png` — live scanner after explicit permission;
3. `03-scan-success.png` — successful scan;
4. `04-devices-linked.png` — `Устройства связаны`/device list;
5. `05-cross-device-error.png` — one test mistake visible on the other platform;
6. `06-restart-without-qr.png` — both clients after restart without onboarding.

Do not fabricate these files from mockups. Remove candidate names, host names, QR secrets and access tokens from captured evidence. `release.yml` refuses to publish when any screenshot is absent.
