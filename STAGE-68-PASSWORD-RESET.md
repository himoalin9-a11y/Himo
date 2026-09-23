# Stage 68 — Email Password Reset

- Added email password reset request endpoint.
- Added 6-digit reset code with 10-minute expiry and 60-second resend limit.
- Added one-time code consumption and failed-attempt protection.
- Added password update using the existing PBKDF2-SHA256 password hashing.
- All previous sessions are revoked after a successful password change.
- User is signed in automatically with a fresh 30-day session.
- Added “نسيت كلمة المرور؟” to the login screen.
- Uses the existing Brevo environment variables: HIMO_EMAIL_API_KEY, HIMO_EMAIL_FROM, HIMO_EMAIL_FROM_NAME.
