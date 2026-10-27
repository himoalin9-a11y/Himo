# Himo.Api

ASP.NET Core 10 API for the Himo MAUI application.

## Production deployment (Render + MongoDB Atlas)

This API stores accounts, sessions, contacts, conversations, messages, message receipts, and push tokens in MongoDB Atlas. The previous PostgreSQL/Npgsql persistence layer has been replaced by `MongoStore` using `MongoDB.Driver`.

### Required Render environment variables

- `MONGODB_URI`: the Atlas connection string. Put credentials only in Render's secret environment settings; never commit a real URI.
- `MONGODB_DATABASE`: database name; the supplied Render Blueprint defaults to `himo`.
- `ASPNETCORE_ENVIRONMENT`: `Production`.

Render sets `PORT` itself (typically `10000`). `Program.cs` binds Kestrel to the injected `PORT`, and `Dockerfile` exposes port `10000` as a container fallback. Do not define a conflicting `PORT` value manually in a Blueprint.

Optional integration variables:

- `HIMO_FIREBASE_SERVICE_ACCOUNT_JSON` enables Firebase Cloud Messaging. Keep the full JSON value as a secret.
- `HIMO_SMS_ACCOUNT_SID`, `HIMO_SMS_AUTH_TOKEN`, and `HIMO_SMS_FROM` are used if SMS verification is enabled in the configured server path.
- `ICE_SERVERS` may be retained for deployments that already use it; the client also has its own WebRTC configuration.

### Render root and Docker paths

If the repository root contains the `Himo.Api` folder, set Render **Root Directory** to `Himo.Api`; then `Dockerfile` and `render.yaml` are relative to that folder. If the repository root itself is the contents of `Himo.Api`, leave Root Directory blank. Do not set it to `Himo.Api/Properties`.

### Health endpoints

- `/health`: liveness endpoint for Render; confirms the process is responding.
- `/health/database`: pings MongoDB and verifies that application indexes can be initialized; returns `degraded` if Atlas connection or index initialization fails.
- `/health/push`: reports Firebase status and registered push-token count.

### First deployment and existing PostgreSQL data

The MongoDB store creates its indexes on first use. The MongoDB change does **not** automatically import records from a previous PostgreSQL/Supabase database; existing accounts, messages, and attachments need a separate migration/export-import if they must be preserved. Do not delete the old database until you have verified MongoDB data and app flows.

### Local build

```powershell
dotnet restore .\Himo.Api.csproj
dotnet build .\Himo.Api.csproj -c Release
dotnet run --project .\Himo.Api.csproj
```

For local execution configure `MONGODB_URI` and `MONGODB_DATABASE` in the process environment. This project targets .NET 10.
