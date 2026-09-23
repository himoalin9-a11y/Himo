# Stage 63 — Final Health Endpoint Isolation

- `/health` is dependency-free and only confirms that Himo.Api is running.
- `/health` does not access PostgreSQL, Firebase, DI services, or external systems.
- `/health/database` performs the separate Supabase PostgreSQL connectivity/schema check.
- This prevents a database configuration/connection problem from making Render's basic health endpoint return HTTP 500.
