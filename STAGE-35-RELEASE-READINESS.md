# Himo — Stage 35 Release Readiness

Before the final device test:
- Build configuration: Release
- Android target/framework must match the installed SDK
- No compiler warnings/errors
- Production API URL must use HTTPS
- Production secrets must be supplied through secure configuration
- Firebase/FCM production configuration must be present
- OTP provider must be configured
- App signing/keystore must be configured
- APK/AAB must be produced from Release
- Final two-device E2E test remains pending until all build stages are closed
