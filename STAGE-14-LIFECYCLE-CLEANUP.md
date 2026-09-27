# Stage 14 — Call lifecycle cleanup

- Removed unused local-invite state from `CallService`.
- Kept call signaling state owned by the active conversation only.
- Removed unused events from the no-op media implementation.
- Preserved the original Stage 7 source structure; no WebRTC API guesses were added.
- `bin/` and `obj/` are excluded from the package.
