# Stage 43 — Professional UI and notification readiness

This stage improves the Android presentation across the main Himo screens and keeps the existing messaging logic intact.

UI:
- Refined Himo visual hierarchy, spacing, cards and headers.
- Improved Home header and conversation section.
- Wired the existing Home conversation search field to its search handler.
- Refined Chat, Login, Search, Profile and Settings presentation.
- Added consistent light/dark resource keys for bubbles, status and danger states.

Notifications:
- Cold-start notification tap routing from the previous stage is retained.
- The Android notification tap stores `conversation_id` until the Shell can navigate to the conversation.
- Fully closed-app push delivery depends on the API server having a valid Firebase Admin service-account credential. The Android `google-services.json` is not a server credential.
- Configure `HIMO_FIREBASE_SERVICE_ACCOUNT` on the API server with the path to the Firebase service-account JSON (or place it at `Himo.Api/App_Data/firebase-service-account.json`).

Validation:
1. Release Rebuild Solution — 0 errors / 0 warnings.
2. Install the APK.
3. Review the main screens visually.
4. Test notifications while the app is backgrounded.
5. Test after swiping the app away from recents.
6. Tap a notification and verify the exact conversation opens.
7. Android Force Stop can block push delivery until the app is launched again.
