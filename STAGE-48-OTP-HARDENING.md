# Stage 48 — OTP security hardening

- OTP values are now stored in SQLite as SHA-256 hashes rather than plaintext.
- OTP verification uses fixed-time byte comparison.
- Existing expiry, resend throttling, and five-attempt limit remain active.
- No API contract or Android UI changes in this stage.
- Before release, rebuild in Visual Studio and verify 0 errors / 0 warnings.
