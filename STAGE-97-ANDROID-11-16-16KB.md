# Stage 97 — Android 11–16 / WebRTC 16 KB compatibility

## Changes made

- The MAUI Android target is explicitly `net10.0-android36.0` so the project compiles against Android 16/API 36.
- The runtime minimum is explicitly Android 11/API 30.
- Android 16 `android:pageSizeCompat="enabled"` is enabled as a compatibility bridge for the current prebuilt WebRTC AAR.
- `Tools/Verify-Android16KB.ps1` was added to verify the final APK's 64-bit ELF LOAD alignment.

## Important limitation

The warning `XA0141` is caused by the prebuilt `libjingle_peerconnection_so.so` shipped inside `FsWebRTC.Bindings.Maui.Android 0.9.3.15`. The .NET project cannot relink that binary. The official .NET for Android guidance is to rebuild/relink the native library with 16 KB alignment.

Therefore `pageSizeCompat` is a compatibility measure, not the final native-library fix. For Google Play compliance and maximum reliability on 16 KB devices, the WebRTC AAR must eventually be replaced by a 16 KB-aligned build.

WebRTC itself has supported 16 KB page size in sufficiently recent upstream builds; the relevant artifact must be rebuilt/packaged with the correct ELF alignment.
