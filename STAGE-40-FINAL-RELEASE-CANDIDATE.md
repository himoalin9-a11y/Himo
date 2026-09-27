# Himo Stage 40 — Final Release Candidate

This stage freezes the current project for final device/server validation.

## Scope
- No new application feature.
- No package changes.
- No `.csproj` changes.
- Preserve the current Stage 39 source as the release candidate baseline.

## Required final validation
1. Rebuild the solution in Visual Studio.
2. Install the Android APK on a real device.
3. Verify login, registration and verification.
4. Verify one-to-one messaging, SignalR reconnect and message states.
5. Verify attachments and audio.
6. Verify mute, archive, block and report flows.
7. Verify notification privacy and FCM notifications.
8. Verify logout from all devices.
9. Verify Render health endpoints and production database connectivity.
10. Keep this build unchanged while performing the final validation.
