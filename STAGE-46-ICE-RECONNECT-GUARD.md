# Stage 46 — ICE reconnect guard

- Prevents ICE state notifications from being raised after the WebRTC engine has stopped.
- Marks the engine stopped before native PeerConnection disposal to avoid stale callbacks during teardown.
