# Stage 47 — Production Server Package

This stage prepares the API for deployment without embedding production secrets in the project.

## Included
- `Himo.Api/SERVER-PUBLISH.bat` publishes the API in Release mode.
- `Himo.Api/PRODUCTION-ENV.template.txt` lists the required production environment variables without real credentials.
- No application logic or Android UI was changed in this stage.

## Required production secrets
- `HIMO_SMS_ACCOUNT_SID`
- `HIMO_SMS_AUTH_TOKEN`
- `HIMO_SMS_FROM`
- `HIMO_FIREBASE_SERVICE_ACCOUNT_JSON` (or a secure service-account file path)
- `ASPNETCORE_URLS`

## Deployment rule
Keep the SMS auth token and Firebase service-account JSON on the server only. Do not place them in the Android project or APK.

## Verification
After publishing the API, verify `/health`. It should report `status: ok`. `pushNotifications: true` means Firebase server credentials were loaded successfully.
