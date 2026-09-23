# STAGE 67 — Email Verification

Himo now uses email verification as the official registration verification flow. Phone/SMS OTP is no longer used by the app.

## Render environment variables
- `HIMO_EMAIL_API_KEY` — Brevo API key
- `HIMO_EMAIL_FROM` — verified sender email in Brevo
- `HIMO_EMAIL_FROM_NAME` — optional sender name, defaults to Himo

Brevo Free currently provides up to 300 email sends per day and requires no credit card.

## Flow
1. Register with email, name and password.
2. API stores a short-lived hashed verification code and pending registration data.
3. API sends the 6-digit code through Brevo.
4. User enters the code.
5. API creates the verified account and session.
6. Login rejects accounts that are not email-verified.

Development mode uses code `123456` so the UI can be tested without sending mail.
