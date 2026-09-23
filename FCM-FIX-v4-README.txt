Himo FCM fix v4

Changes:
1. Keeps the existing FCM server payload and push-token registration.
2. Configures the Android FCM notification channel during MAUI startup as well as MainActivity startup.
3. Makes notification-tap handling read conversation_id both directly from the Android Intent and from nested FCM Bundles.
4. Keeps the existing Plugin.Firebase.CloudMessaging OnNewIntent integration.
5. Does not include or modify the Firebase service-account private key.

Important Android testing note:
- "Swiping the app away" and Android Settings > Force stop are different.
- A Force Stop can prevent FCM delivery until the app is launched again on some Android devices.
- Test the fully closed case by closing/swiping the app from Recents, not by pressing Force stop.


Himo v13: improved FCM token registration resilience with guarded retries when the API/network is temporarily unavailable. No MSBuild/intermediate-output changes.
