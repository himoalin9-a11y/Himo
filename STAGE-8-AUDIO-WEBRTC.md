# Stage 8 — WebRTC audio foundation

Started from the original Stage 7 source.

Implemented:
- Android WebRTC binding loading.
- Audio factory/source/track creation at the binding boundary.
- Microphone track enable/disable.
- Native object cleanup.
- No direct references to generated `VideoCapturer`, `CameraVideoCapturer`, `DeviceNames`, or obsolete `EglBase` APIs.
- Video is deliberately not included in Stage 8.

Next: Stage 9 video/camera and local/remote rendering.
