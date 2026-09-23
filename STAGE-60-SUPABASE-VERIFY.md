# Stage 60.1 — Supabase health check hardening

The `/health` endpoint now performs a single safe PostgreSQL connection check and an explicit `information_schema` table check. It no longer relies on PostgreSQL array-parameter inference for the schema query, and database health failures are reported as degraded JSON instead of surfacing the generic 500 response.
