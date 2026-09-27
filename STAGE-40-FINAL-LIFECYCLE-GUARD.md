# Stage 40 — Final negotiation lifecycle guard

- Prevents a stale `StateChanged` notification from recreating a WebRTC negotiation session after the call has already been cleared.
- Stops any existing negotiation session when the call service no longer has the same active conversation.
- Keeps the existing same-conversation/same-mode idempotent behavior.
