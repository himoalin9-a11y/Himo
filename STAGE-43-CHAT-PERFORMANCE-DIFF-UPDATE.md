# Stage 43 — Chat Performance Diff Update

- Avoids rebinding the chat CollectionView when a 30-second polling request returns no new messages.
- Avoids repeated GetMessages calls when a realtime message arrives.
- Uses non-animated scroll for realtime insertion to reduce UI work.
- No package or project-file changes.
- No feature removal.
