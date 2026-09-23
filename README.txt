Himo-v157 network fix v2

Corrected after build feedback.

1. HimoApiClient.cs is based on the FCM-compatible v165 client and RETAINS:
   - RegisterPushTokenAsync
   - RemovePushTokenAsync
   - all existing conversation/message/auth/profile methods
2. HTTP transport is switched to SocketsHttpHandler with UseProxy=false and a 10s ConnectTimeout.
3. Himo.csproj sets UseNativeHttpHandler=false.
4. LoginPage.xaml.cs keeps the existing DEBUG diagnostic output.

Separate build issue:
Himo.Api.exe is locked by the running Himo.Api process. Stop that process before rebuilding the whole solution.
PowerShell: Stop-Process -Id 17860 -Force
(If the PID has changed, stop the currently running Himo.Api process instead.)

The GoogleCredential.FromFile and MulticastMessage.Tokens messages are warnings, not build blockers.
