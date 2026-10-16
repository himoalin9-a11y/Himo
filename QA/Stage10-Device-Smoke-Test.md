# Himo — Stage 10 Device Smoke Test

## Purpose
Final validation checklist for the real Android device before the release build.

## Test order
1. Install/launch app and confirm splash/startup.
2. Login / logout.
3. Home: refresh, empty state, navigation.
4. Search: search, loading state, open result.
5. Profile: edit and save.
6. Settings: notification/app-lock/dark-mode controls and navigation.
7. App Lock: enable, lock, unlock, wrong PIN handling.
8. Chat: open conversation, send text, receive text, scroll, read state.
9. Chat attachments: image/file selection, preview, open attachment.
10. Voice: start/stop recording, send, play received audio.
11. Background/foreground: verify chat reconnects and UI remains responsive.
12. Notifications: receive notification, tap it, verify destination.
13. Keyboard: open/close, send from keyboard, verify composer is not obscured.
14. Rotation/window resize if supported by the test device.

## Record for each item
- PASS / FAIL
- Device model
- Android version
- Build/version installed
- Screenshot for every FAIL
- Exact reproduction steps for every FAIL

## Release gate
Do not treat the app as device-validated until the smoke test has been executed on the real device. Static checks alone do not validate rendering, touch behavior, keyboard behavior, audio recording, notifications, or network timing.
