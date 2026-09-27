# Himo — Stage 7: Native Android WebRTC Binding Gate

## Goal
Move from a placeholder media boundary toward the real Android libwebrtc stack without coupling the call UI directly to generated Java bindings.

## Changes
- Added `FsWebRTC.Bindings.Maui.Android` 0.9.3.15 for `net10.0-android`. The package ships Android `libwebrtc.aar` bindings and is published as compatible with .NET 10 Android.
- Added `AndroidWebRtcMediaEngine` behind `IWebRtcMediaEngine`.
- The engine verifies the native binding is present at media-call start and keeps native generated types isolated from the rest of Himo.
- `CallService` now owns the lifecycle boundary: start/stop and microphone/speaker state are forwarded to the WebRTC engine as well as the Android audio-routing controller.
- Non-Android targets retain the no-op implementation.

## Deliberate limitation
This stage does **not** claim that RTP audio/video is working yet. The exact generated `org.webrtc` surface must be validated on a real Android build before wiring PeerConnectionFactory, audio tracks, camera capture, and renderers.

## Next gate
Stage 8 should create the Android `PeerConnectionFactory` and local audio track behind a second adapter, then validate a one-device local media startup before connecting it to the existing Offer/Answer/ICE signaling. Do not move to group calls until one-to-one media is device-tested.
