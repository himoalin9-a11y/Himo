# Stage 13 — Chat Reliability

- Use `SenderUserId`/`Account.UserId` to determine message ownership.
- Preserve the legacy root server files as non-compiled compatibility copies; the active server remains `Himo.Api`.
- Existing accounts without a stored `UserId` are synchronized from `/api/me` after startup.
- Authentication responses carry the server user ID so new sign-ins persist identity immediately.
