# Stage 15 — Compiler Fix

- Started from the original Himo-stage7 source.
- Removed compile-time `VideoCapturer` / `CameraVideoCapturer` dependencies.
- Replaced Camera2 `DeviceNames` property usage with the actual `GetDeviceNames()` binding method through reflection.
- Fixed SDP and PeerConnection observer nullability signatures.
- Added the required MAUI `ViewHandler` mapper/constructor.
- Suppressed only the obsolete `EglBase` compiler warning while keeping the existing WebRTC implementation intact.
- Removed `bin` and `obj`.
