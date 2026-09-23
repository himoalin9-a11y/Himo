# Himo v201 — Stage 59
# Stage 59 — Supabase PostgreSQL persistence

- Replaced the hosted API SQLite store with PostgreSQL through Npgsql.
- The API reads `DATABASE_URL` from the hosting environment.
- Supabase PostgreSQL schema is created automatically at startup.
- Existing message/session/auth endpoints continue using the same store abstraction.
- Render no longer depends on a writable local database directory.
- `/health` now reports `database: true/false`.
- Firebase FCM remains independent of the database provider.

## Render

Set `DATABASE_URL` to the Supabase PostgreSQL connection string in Render Environment Variables. Keep the value secret.

## Verification

After deployment, open `/health`. Expected response contains `"database":true`.
