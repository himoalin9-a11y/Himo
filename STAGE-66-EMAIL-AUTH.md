# STAGE 66 - Email registration and login

This stage replaces the Himo mobile login UI with email + password authentication.

- Registration: email, display name, password (minimum 8 characters).
- Login: email + password.
- Passwords are never stored as plain text; the API stores PBKDF2-SHA256 password hashes with per-user random salts.
- Existing PostgreSQL `Users` data is preserved.
- On API startup, `PasswordHash` is added to `Users` automatically when it does not exist.
- The existing phone OTP code remains in the source for backward compatibility, but the mobile login screen no longer uses SMS OTP.
- User search now uses email.
- Chat ownership checks use the signed-in email while the legacy database/message column names remain unchanged to avoid breaking existing conversation data.

Deployment order:
1. Deploy the updated Himo.Api to Render.
2. Keep the existing Supabase DATABASE_URL unchanged.
3. Build the Android app in Release.
4. Test: create account -> logout -> login -> search by email -> start chat -> send message.
