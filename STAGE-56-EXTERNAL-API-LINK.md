# Himo — Stage 56: External API Link

## Completed

- Android default API endpoint is now the external Render HTTPS endpoint:
  `https://himo-3buh.onrender.com/`
- Existing installations using the previous LAN address `192.168.8.85:5080` are automatically migrated to the external endpoint.
- Existing installations using `localhost` or `127.0.0.1` are also migrated to the external endpoint.
- Settings remains available for explicitly selecting another API endpoint during development.
- SignalR continues to derive `/hubs/chat` from the configured API base URL, so it follows the external HTTPS endpoint automatically.
- Login and Settings examples now show the external HTTPS endpoint.

## Release version

- Android `ApplicationDisplayVersion`: `10.9`
- Android `ApplicationVersion`: `100`

## Verification gate

1. Rebuild Release Any CPU.
2. Confirm 2 succeeded, 0 failed, 0 skipped, with no compiler/XAML warnings.
3. On the Android device, launch Himo and verify the server URL resolves to `https://himo-3buh.onrender.com/`.
4. Verify `/health` remains reachable externally.
5. Do not perform the final two-device test until the remaining project stages are complete.
