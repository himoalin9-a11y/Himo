# Himo — Production Push Notifications

## Stage 45

The API now supports three secure server-side Firebase credential sources:

1. `HIMO_FIREBASE_SERVICE_ACCOUNT_JSON` — full service-account JSON stored as a server secret.
2. `HIMO_FIREBASE_SERVICE_ACCOUNT` — path to the service-account JSON file.
3. `GOOGLE_APPLICATION_CREDENTIALS` — standard Google credential path.
4. Local fallback: `Himo.Api/App_Data/firebase-service-account.json`.

Do **not** put a Firebase service-account JSON file inside the Android application and do not commit it to source control.

The `/health` endpoint now reports `pushNotifications: true/false` without exposing credentials.

For closed-app delivery, the production API must have valid Firebase service-account credentials and the Android device must have notification permission enabled.
