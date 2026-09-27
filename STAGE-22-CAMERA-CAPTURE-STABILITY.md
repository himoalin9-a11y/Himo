# Stage 22 — Camera Capture Stability

- Removed the duplicate `StartCapture` call during video initialization.
- Camera capture now starts once per video-session initialization.
- No changes to EGL, signaling, or renderer contracts.
