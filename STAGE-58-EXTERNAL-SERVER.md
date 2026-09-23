# Himo — Stage 58: External Server + Firebase Preparation

## Completed in this stage

- Preserved the existing ASP.NET Core API and SignalR architecture.
- Kept Firebase Cloud Messaging (FCM) as the push-notification provider.
- Added `HIMO_DATA_DIR` so the SQLite database location can be supplied by the hosting environment.
- Added a Linux/Docker deployment path for the API.
- Added a Render deployment manifest for a card-free external-hosting test path.
- Kept Firebase service-account credentials server-side only.
- Kept the Android client free of server Firebase private credentials.

## Important hosting note

The Render `free` service is intended for external testing. Its filesystem is ephemeral, so the local SQLite database must not be treated as permanent production storage there. For a production deployment, move the database to durable external PostgreSQL storage (for example Supabase) or use a host with persistent storage.

## Firebase

The Android project already contains `Platforms/Android/google-services.json` and the FCM client integration. The API already supports these server-only credential sources:

1. `HIMO_FIREBASE_SERVICE_ACCOUNT_JSON`
2. `HIMO_FIREBASE_SERVICE_ACCOUNT`
3. `GOOGLE_APPLICATION_CREDENTIALS`
4. `Himo.Api/App_Data/firebase-service-account.json`

Never place the Firebase service-account JSON in the Android application.

## Next deployment gate

1. Create the external hosting account.
2. Deploy `Himo.Api` using the supplied Docker configuration.
3. Verify `https://<public-host>/health` returns `status=ok`.
4. Configure the Android API URL to the public HTTPS address.
5. Verify login, messaging, SignalR, and FCM.
6. Only after that move persistent message data from SQLite to Supabase/PostgreSQL if the selected free host cannot provide durable storage.
