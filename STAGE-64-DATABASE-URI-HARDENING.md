# STAGE 64 - PostgreSQL connection URI hardening

The API now accepts PostgreSQL/Supabase URLs even when URL-special characters appear in the credential portion. It parses the authority without exposing credentials and then builds a normal Npgsql connection string.

Render should keep `DATABASE_URL` as the Supabase PostgreSQL connection URL. No password is stored in the repository.
