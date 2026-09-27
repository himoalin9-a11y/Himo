# Himo — Stage 5: Media Foundation

## Implemented
- Added `ICallMediaController` so call-audio hardware is isolated from signaling/UI.
- Added Android implementation using `AudioManager` for communication mode, microphone mute, and speaker routing.
- `CallService` now starts/stops the Android call-audio route with the call lifecycle.
- Mute and speaker buttons now control Android audio routing instead of only changing UI state.
- Kept `WebRtcSession` transport-neutral; native WebRTC media is not falsely represented as complete.
- Removed an unused import from the Stage 4 WebRTC session.
- Version bumped to 11.9 / 119.

## Deliberately not claimed
This stage does **not** claim that microphone samples, remote RTP audio, or camera frames are already transported by WebRTC. The currently researched `SpawnDev.RTC` package targets .NET 10 and exposes WebRTC APIs, but its published architecture emphasizes browser/desktop implementations; Android-native media capture still requires device validation before coupling it to Himo.

## Next gate
Stage 6 should add the actual Android-compatible WebRTC media transport only after the package/native binding is verified on a real Android device. One-to-one audio must be verified before video and group calls.
