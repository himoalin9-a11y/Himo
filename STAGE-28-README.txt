HIMO — STAGE 28
================

Realtime Chat / SignalR

Implemented:
- ASP.NET Core SignalR hub at /hubs/chat.
- Existing 64-character session token authenticates SignalR connections.
- Per-user SignalR groups prevent messages from being broadcast to unrelated users.
- New text and attachment messages are pushed to the recipient immediately after persistence.
- Android client uses Microsoft.AspNetCore.SignalR.Client with automatic reconnect.
- ChatPage consumes realtime messages and updates the visible conversation immediately.
- Existing incremental HTTP sync remains as a 15-second fallback for offline/reconnect recovery.
- Server push notifications remain available for background/closed-app delivery.

Testing is intentionally deferred until the complete build is finished.
