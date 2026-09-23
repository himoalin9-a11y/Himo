# Stage 50 — App Lock

- Added optional 4–6 digit app PIN lock.
- PIN is stored as a salted HMAC-SHA256 hash in Android SecureStorage.
- App locks after returning from background after 30 seconds.
- App starts on the lock screen when a signed-in session has app lock enabled.
- Lock can be enabled/disabled from Settings.
