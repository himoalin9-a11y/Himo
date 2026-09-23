# Stage 44 — Production OTP / SMS readiness

This stage removes the development-only authentication dependency from the API.

## Behavior
- Development keeps the fixed `123456` test code.
- Non-development environments generate a cryptographically secure six-digit OTP.
- The OTP is stored only after the normal one-minute resend guard passes.
- The API sends the OTP through the configured SMS provider.
- If SMS configuration is missing or the provider fails, the temporary OTP is removed so the user is not blocked by a code that was never delivered.
- OTP verification is no longer disabled outside Development.

## SMS configuration
The current implementation uses Twilio's HTTPS API without putting credentials in the app or source code.

Set these environment variables on the API server:

- `HIMO_SMS_ACCOUNT_SID`
- `HIMO_SMS_AUTH_TOKEN`
- `HIMO_SMS_FROM`

Use an SMS-capable Twilio sender for `HIMO_SMS_FROM` and provide phone numbers in international format (for example `+966...`).

Do not commit these values to Git or place them in the Android application.

## Still required before production
- Production HTTPS/domain for Himo.Api.
- Real Firebase Admin service-account credential for FCM push delivery.
- Real SMS/Twilio credentials.
- Production database backup/storage plan.
- Release signing credentials.
