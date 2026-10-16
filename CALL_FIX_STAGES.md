# Himo call-lifecycle patch — staged pre-install validation

This archive contains one coordinated patch, not separate test APKs. Extract it to a folder **outside** the Himo project, then run `Apply-HimoCallPatch.ps1` against the project directory. The script backs up the 13 touched files, applies the patch, builds both Release projects, and restores the previous files if either build fails. It does **not** install the APK or use ADB.

## Stages included

1. **Signal path:** FCM dispatch runs independently of the SignalR hub invocation so a push send cannot delay or cancel an `Answering`, `Reject`, or `End` signal.
2. **Call identity and termination:** Call messages use no per-call FCM collapse keys (FCM limits the number of active keys); Invites expire after 45 seconds, while End/Reject cancellation pushes live for 2 minutes and get a longer server-side send timeout, so a delayed offline delivery is less likely to lose the cancellation. Late invites for finished calls are suppressed by CallId tombstones; remote end pushes clear the notification and active call page; ringtone has a bounded failsafe.
3. **Single answer path:** duplicate/ambiguous call-page navigation is guarded; preparing an identical call page no longer invalidates the in-progress answer operation; native-start races check that the same call is still active.
4. **Answer latency:** the incoming page warms the SignalR channel and WebRTC factory while ringing; answer does not wait up to the reconnect timeout before starting local media.
5. **Android 14+ full-screen access:** at startup, Himo checks whether Android allows full-screen notification intents. If access is unavailable, it offers a direct link to the system setting. Incoming-call notifications use a high-priority notification fallback when special access is denied.
6. **Pre-install gate:** the included script uses an explicit `Rebuild` for both Release projects (single MSBuild worker), then requires a non-empty API DLL and a newly timestamped Release APK created after that rebuild began. It also requires a passing `Tools/Verify-Android16KB.ps1` check for the WebRTC native library before it reports `BUILD PASS`. This prevents an older DLL/APK from satisfying the gate when source timestamps are older than existing outputs. On any build or validation failure it restores the 13 touched source files. The script never installs the APK.
7. **Race-proof notification teardown:** posting an incoming-call notification and handling `Answering`/`Accept`/`Reject`/`End` are serialized by the same gate. The terminal state records a tombstone, removes the active marker, and cancels the notification under that gate; an older invite re-checks the tombstone immediately before posting. This specifically closes the race where a late Invite callback could re-post a notification after End had cleared it.
8. **No stale post-answer work:** after `AcceptAsync` returns, the call page verifies the active conversation and CallId again before clearing the notification or attaching video. If a remote end arrived during native media startup, the page does not continue work for the terminated call.
9. **FCM answer-state fallback:** `Answering`/`Accept` pushes notify an active outgoing call page to stop ringback and update its status without terminating the call. This covers the case where SignalR's state message is delayed but Firebase push arrives first; `Reject`/`End` remain terminal events.

## Run without installing

From PowerShell in the extracted patch folder:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Apply-HimoCallPatch.ps1 -ProjectRoot "E:\Downloads\Himo"
```

A successful run ends with `BUILD PASS`, prints the APK path and SHA-256, and completes the native 16 KB alignment check. No installation is performed. Keep the backup folder printed by the script until the final phone test is complete.

## Validation boundary

The patch archive, Android manifest XML, cross-file contracts, and pre-install script's forced-rebuild/fresh-artifact gate were checked statically here. The notification ordering now has a common synchronization gate for both posting and terminal cancellation, the post path performs a final call-ID tombstone check, terminal FCM states outlive the invite TTL, and the answer path re-checks that the same call is still active after awaited native initialization. This environment does not have the .NET SDK, so it cannot independently compile the patched C# files or test Android behavior on two physical phones. A successful build is a necessary pre-install gate, but it does not by itself prove call audio, FCM delivery, device-specific full-screen-intent access, or behavior when the app has been force-stopped. Those remain for the final two-phone test.
