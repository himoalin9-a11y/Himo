Himo final chat stability repair - 2026-09-29

This package is the full Himo project with the chat opening/performance repair applied.

Main repairs:
- Chat opens from local cache without waiting for Render/HTTP/SignalR.
- Initial visible window is the newest 20 messages.
- Older messages are revealed locally first when already cached, then fetched from the server in pages of 20.
- Server message history is never deleted because of the visible 20-message window.
- Local ChatService no longer trims message history to 20/1000/other cache limits.
- Initial server history query returns newest messages instead of oldest messages.
- API message page is limited to 20 rows.
- Polling fallback is 5 seconds.
- SignalR startup and history sync run in the background.
- Automatic image downloads were removed from history/realtime synchronization.
- Message timestamps used for sync cursors are converted to UTC before API requests.
- CollectionView no longer uses KeepLastItemInView; scrolling to the newest message is explicit.
- CA1416 warning suppression remains in Himo.csproj as previously requested.

Login, login UI logic, and WebRTC implementation were not intentionally changed by this repair.

Important:
Build/test with Visual Studio on the target Android device. The repair was statically checked here, but a local .NET SDK build was not available in this environment.
