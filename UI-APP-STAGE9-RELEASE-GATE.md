# Himo — App UI Stage 9

## Release Gate / Device-Test Preparation

This stage is the final static gate before installing the APK on a real Android device.

### Checks completed
- Reviewed all application XAML screens under `Views/`.
- Reviewed XAML button event handlers against their corresponding code-behind methods.
- Checked for duplicate `x:Name` declarations within individual XAML files.
- Checked that the MAUI project excludes the embedded `Himo.Api/` server source tree from the client compilation path.
- Reviewed shared UI resources in `App.xaml` and the screen-level styles for consistency.
- Preserved ChatPage's dedicated Stage 6 visual tuning.
- Preserved API, SignalR, authentication, database and navigation behavior.
- No source-code build was claimed: the current environment does not contain the .NET SDK.

### Device-test gate
The remaining validation is runtime validation on a real Android phone. Static inspection cannot verify exact rendering, keyboard behavior, touch targets, Android back behavior, notification behavior, microphone permissions, or device-specific spacing.

### Suggested runtime order
1. Launch / login.
2. Home and navigation.
3. Search and open a conversation.
4. Send text and attachment messages.
5. Receive a message through SignalR.
6. Record and play an audio message.
7. Profile save.
8. Settings / app lock / logout.
9. Android notification and permission behavior.
10. Final screenshots for visual matching.
