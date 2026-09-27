# Stage 75 — Remote Audio Service Guard

- Serialized remote-audio state changes in `CallService` to prevent overlapping requests.
- Re-checks current call/state before publishing the new remote-audio state.
