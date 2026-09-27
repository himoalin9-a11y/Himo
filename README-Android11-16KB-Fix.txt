Himo Android 11-16 / WebRTC 16 KB fix

Changes in this package:
1. ChatPage uses absolute Shell routing (///CallPage) for audio/video calls, fixing the crash reported by AndroidRuntime: Relative routing to shell elements is currently not supported.
2. Android minimum supported API is 30 (Android 11); target framework is net10.0-android36.0 (Android 16/API 36).
3. The CA1416 PendingIntentFlags.Immutable warning is resolved by the Android 11 minimum platform declaration.
4. Tools/Install-WebRTC16KB.ps1 is included to validate and replace FsWebRTC's AAR with WebRTC 144.7559.15, whose arm64-v8a and x86_64 libraries were verified as 16 KB ELF aligned in the current workflow.

Important:
- The WebRTC AAR is not embedded in this source archive. Run the installer after NuGet restore if the package cache has been restored.
- After applying this source, build with: dotnet clean .\Himo.sln ; dotnet build .\Himo.sln --no-restore
