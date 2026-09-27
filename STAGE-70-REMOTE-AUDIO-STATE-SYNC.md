# Stage 70 — Remote Audio State Sync

- CallState now carries remote-audio enabled state.
- CallPage reflects remote-audio state in the control button and receives updates through StateChanged.
- Toggling remote audio updates CallService state immediately.
