# Stage 61 — Health endpoint and PostgreSQL initialization fix

The previous health failure was caused by resolving `PostgresStore` during `/health` while its constructor immediately attempted database schema initialization. Any connection/initialization exception was therefore converted by the global exception handler into the generic 500 response.

This version changes database initialization to lazy initialization:
- Constructing `PostgresStore` no longer opens PostgreSQL or creates tables.
- `/health` performs a raw connection and schema check and returns a degraded JSON response instead of triggering schema initialization.
- Normal database operations initialize the schema once, on first real database use.
- Firebase FCM initialization remains compatible with the service.
