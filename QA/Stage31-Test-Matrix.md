# Himo — Stage 31 Test Matrix

## Purpose

Stage 31 adds a repeatable smoke-test layer and defines the full QA matrix. Full device/E2E execution is intentionally deferred until all build stages are complete.

## Automated smoke checks

Run `QA/Stage31-SmokeTest.ps1` while `Himo.Api` is running. The script verifies:

- `/health` returns HTTP 200 and `{ status: "ok" }`.
- `/api/me` rejects unauthenticated requests.
- `/api/me` rejects malformed bearer tokens.
- `/api/conversations` rejects unauthenticated requests.

Example:

```powershell
.\QA\Stage31-SmokeTest.ps1 -BaseUrl "http://localhost:5000"
```

## Final E2E matrix (run after Stage 32)

| Area | Scenario | Expected |
|---|---|---|
| Auth | Request OTP | Code request succeeds in Development; production uses real SMS provider |
| Auth | Verify OTP | Session is created and persisted |
| Auth | Invalid OTP | Request is rejected |
| Auth | Logout | Session is invalidated |
| Profile | Read/update profile | Changes persist after restart |
| Search | Search by phone | Correct user returned |
| Chat | Create 1:1 conversation | Conversation created/reused |
| Chat | Send text | Sender and receiver see one message |
| Chat | Duplicate client ID | Message is not duplicated |
| Realtime | SignalR delivery | Online recipient receives immediately |
| Reconnect | Drop/recover network | Client reconnects and syncs missing messages |
| Read state | Open conversation | Messages become read |
| Notifications | App background | FCM notification arrives |
| Notifications | Tap notification | Correct conversation opens |
| Attachments | Image | Upload, thumbnail/preview, download |
| Attachments | File | Upload and open/download |
| Attachments | Oversize | Request rejected above 25 MB |
| Voice | Record/play | Audio records, uploads, and plays |
| Security | Unauthenticated API | Protected endpoints return 401 |
| Security | Invalid bearer | Rejected before database access |
| Security | Path traversal filename | Stored filename remains inside uploads |
| Account | Delete account | User/session/data policy is applied consistently |
| Restart | Kill/reopen app | Local state and sync remain correct |
