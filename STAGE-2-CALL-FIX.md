# Himo — Stage 2: Call Transport and WebRTC Startup Fix

This stage fixes the call startup path without changing the chat/message UI.

## Changes
- SignalR call signaling now waits for a fully connected hub instead of treating `Reconnecting` as ready.
- CallPage waits for the signaling channel before starting the media session.
- WebRTC `PeerConnectionFactory` is explicitly initialized before the first factory is created, with compatibility fallback for older bindings.
- Call startup errors are shown with the actual reason instead of the generic `تعذر تجهيز الاتصال`.
- Login server response remains fixed: email login returns the actual email instead of `PhoneNumber`.

## Important
The project must be built on Windows with the Android SDK/NDK installed. The WebRTC 16 KB installer from the previous stage must be run after NuGet restore if restore replaces the cached AAR.
