# Stage 44 — ICE candidate payload

- Local ICE candidates are serialized as JSON with `sdpMid`, `sdpMLineIndex`, and `candidate`.
- Remote ICE accepts the new JSON payload and the previous `mid|mLine|candidate` format.
- Duplicate remote ICE candidates are ignored before reaching the native PeerConnection.
