# Himo - Firebase Cloud Messaging

This version enables Firebase Cloud Messaging (FCM) for Android and connects push notifications to the existing Himo conversation screen.

## Android client

- Firebase package: `Plugin.Firebase.CloudMessaging` 4.0.1.
- Firebase configuration: `Platforms/Android/google-services.json`.
- Android package name: `com.companyname.himo`.
- FCM token is registered with the Himo API after sign-in and refreshed when Firebase changes it.
- Notification taps carry `conversation_id` and open the matching Himo chat.

## Server push credentials

The API sends FCM notifications using the Firebase Admin .NET SDK. **Do not put a service-account private key inside the mobile app or commit it to source control.**

1. In Firebase Console, open Project settings -> Service accounts.
2. Generate a new private key for the Firebase service account.
3. On the Windows PC running `Himo.Api`, place the downloaded JSON at:

   `Himo.Api/App_Data/firebase-service-account.json`

   Keep this file private. The application reads it only on the server.

Alternatively set the environment variable `HIMO_FIREBASE_SERVICE_ACCOUNT` to the full path of the JSON file.

If the service-account file is absent, Himo continues to work normally, but the API will log that FCM push delivery is disabled.

## Important

The existing polling notifications remain in place for foreground use. FCM provides delivery when the app is in the background or has been removed from the recent-apps screen.
