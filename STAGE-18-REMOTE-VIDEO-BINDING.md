# Stage 18 — Remote Video Binding

- Bind a newly received remote video track immediately when the remote renderer is already attached.
- Remove the previous track from the renderer before replacing it.
- No changes to EGL, camera capture, signaling, or call lifecycle.
