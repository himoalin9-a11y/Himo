# Stage 20 — WebRTC Renderer Reattach

The Android WebRTC media engine now reuses an existing local/remote renderer and reattaches it to the current MAUI native host when the visual host is recreated or moved.

No change to signaling, EGL initialization, camera capture, or WebRTC track creation.
