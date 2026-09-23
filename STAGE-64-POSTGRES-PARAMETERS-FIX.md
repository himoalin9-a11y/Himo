# Himo v218 - PostgreSQL parameter fix

Fixed the PostgreSQL/Npgsql parameter placeholder mismatch in Himo.Api.

All named SQL parameters now use the Npgsql-compatible `@name` form consistently with the corresponding `AddWithValue("@name", ...)` parameters. This fixes PostgreSQL syntax errors such as `syntax error at or near "$"` during email verification and other database operations.

No changes were made to Brevo configuration, DATABASE_URL, UI, authentication flow, or database schema.
