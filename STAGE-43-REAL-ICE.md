# Stage 43 — Real ICE wiring

- Native `PeerConnection.IObserver` is now attached to the real PeerConnection.
- Locally gathered ICE candidates are serialized and sent through the existing call signaling path.
- Remote ICE candidates are queued until the remote SDP is applied, then added to the native PeerConnection.
- STUN server configuration is enabled for candidate gathering.
