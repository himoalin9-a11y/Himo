# Stage 49 — Final Release Audit

This stage performs release-package hygiene only. No application logic is changed.

## Release checks
- Build configuration: Release
- Android package format: APK
- Himo and Himo.Api must rebuild with 0 errors and 0 warnings.
- No `.vs`, `bin`, or `obj` directories are included in the source ZIP.
- No Firebase service-account JSON should be committed into the Android app or source package.
- Production SMS/FCM credentials must be supplied through server environment variables.
- Final device testing is required after the APK is published.

## Before production deployment
1. Configure the production API URL.
2. Configure SMS credentials on the server.
3. Configure Firebase service-account credentials on the server.
4. Verify `/health` reports the expected production services.
5. Publish the Release APK.
6. Install it on a clean Android device and perform the complete functional test.
