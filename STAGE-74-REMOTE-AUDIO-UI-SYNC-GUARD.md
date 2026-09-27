# Stage 74 — Remote Audio UI Sync Guard

- Added a UI state version guard to prevent stale queued CallStateChanged updates from overwriting newer call/remote-audio state.
- Invalidated pending UI updates when the call page disappears.
