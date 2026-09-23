# Himo — Production Stage

## Stage 34
Production configuration hardening.

Before final release:
1. Configure a real HTTPS domain for Himo.Api.
2. Configure a real SMS/OTP provider and keep credentials outside source control.
3. Configure Firebase/FCM server credentials securely.
4. Set production database connection settings through environment/secret storage.
5. Configure upload storage and limits for production.
6. Publish Android Release only after the server configuration is ready.
7. Perform the complete two-device E2E test only after all build stages are closed.

No production secrets are included in this package.
