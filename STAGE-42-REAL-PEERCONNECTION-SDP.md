# Stage 42 — Real PeerConnection Offer/Answer

- Added native SDP offer/answer operations to the Android WebRTC engine.
- Local SDP is set on the native PeerConnection before signaling it.
- Remote Offer is applied, then an Answer is created and signaled.
- Remote Answer is applied to the existing native PeerConnection.
- Kept all binding-specific calls behind reflection/Android WebRTC types.
