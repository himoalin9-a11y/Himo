# Stage 51 — Release Candidate QA

This stage freezes the application code after the clean Release rebuild and prepares the final device-test sequence.

## Build gate
- Release / Any CPU rebuild must remain 2 succeeded, 0 failed, 0 skipped.
- Treat linker/AOT optimization messages as informational build output, not compilation errors.
- Do not publish or install an intermediate build until the complete feature set is accepted.

## Final device test sequence
1. Install the Release APK on Device A and Device B.
2. Configure both devices to use the reachable API server address, not localhost.
3. Register/login on both devices.
4. Send text messages in both directions.
5. Verify unread/read state and conversation refresh.
6. Verify attachment messaging.
7. Verify FCM delivery while the app is backgrounded.
8. Tap a notification and verify the exact conversation opens.
9. Close the app normally and verify the documented Android notification behavior.
10. Enable App Lock, leave the app for at least 30 seconds, return, and verify the lock screen.
11. Tap a notification while App Lock is active; unlock and verify the pending conversation opens.
12. Disable App Lock and verify normal startup/resume behavior.
13. Verify logout, sign-in again, and push-token registration.

## Production requirements
- Firebase Admin service-account credentials stay on the API server only.
- Android uses its Firebase client configuration only.
- The API must be reachable from the devices over the configured network.
- `/health` should report `status: ok`; `pushNotifications: true` requires valid server-side Firebase credentials.

Full two-device functional testing remains the final gate after the project is complete.
