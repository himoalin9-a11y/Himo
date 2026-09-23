# Himo v54

.NET MAUI Android client for Himo with the Himo.Api backend.

## v44
- Added pull-to-refresh on the conversations list.
- Refresh uses the existing server synchronization without adding packages.
- Refresh state is always cleared even when the server is unavailable.
- Kept the existing notifications, unread counters, profile sync, and API fixes.

## Firebase Cloud Messaging
A real `google-services.json` for package `com.companyname.himo` is required before FCM can be activated. No fake Firebase credentials are included.


## v47
- Stabilized local conversation IDs during server refresh.
- Existing cached messages now remain attached to their conversation when server ordering changes.
- Unread counts are preserved by remote conversation ID.


## Current phone development connection
The current physical-phone development build defaults to `http://192.168.8.85:5080/` because the development PC is reachable at that LAN address. The phone and PC must remain on the same network.


## v54
- Improved one-to-one conversation behavior.
- Existing one-to-one conversations are reused instead of creating duplicates.
- The participant's current profile name is used as the display name for two-person conversations.
- Starting a user conversation now reports server errors instead of silently creating a disconnected local chat.
- Search results now expose the user's initial correctly.
- Chat header no longer labels normal server conversations as local.


## v10 improvement
- Notification taps can now carry the server conversation GUID and the chat page resolves it to the local conversation ID before loading.


Phase 12 v63: Added a clear "بدء محادثة جديدة" action to the Home empty state.


## v162 / Stage 33
- Himo.Api now honors `ASPNETCORE_URLS` when supplied by the hosting environment.
- Local development keeps the existing `http://0.0.0.0:5080` fallback.
- Production hosting can now choose its bind address without changing source code.

## Stage 44
Production authentication now supports a real SMS OTP path through server-side Twilio configuration while retaining the fixed development OTP for local development. See `STAGE-44-PRODUCTION-OTP.md`.

## Stage 58 — External server preparation

The API now supports a configurable persistent data directory through `HIMO_DATA_DIR` and includes a Linux Docker deployment path under `Himo.Api/`. Firebase FCM remains server-side and its service-account credentials must only be configured on the server.
