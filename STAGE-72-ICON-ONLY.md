# Stage 72 — Himo Hoopoe App Icon Only

- This stage changes the launcher/app icon only.
- The app icon is based on the approved Himo hoopoe artwork with the white `Himo` wordmark.
- The icon is supplied as a single PNG to avoid the previous MAUI foreground/background SVG composition and to prevent the icon artwork from being altered by a separate foreground layer.
- No login, email verification, Brevo, API URL, database, authentication, chat, settings, or other application behavior was changed.
- UI redesign is intentionally NOT included in this stage.
- Before rebuilding in Visual Studio: close Visual Studio, delete `bin` and `obj`, reopen the solution, then Rebuild Release. If the old icon remains on Android, uninstall the old Himo app before installing the new APK.
