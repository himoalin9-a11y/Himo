# Stage 68 — Remote Audio Control

- Added a platform-neutral remote-audio enabled state to `IWebRtcMediaEngine`.
- Android now applies the state directly to the received WebRTC `AudioTrack`, preserving it when the same track is rebound and resetting it with the session lifecycle.
