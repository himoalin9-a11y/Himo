# Himo Release 2 — Login 3-Phase Fix

## Phase 1 — Startup stability
- Login page changes are isolated from the startup/session services.
- App startup now validates the persisted account/token pair defensively.
- A stale or inconsistent session is cleared instead of entering a crash loop.
- AppShell/AppLock creation is guarded; if an optional persisted session page fails, the app returns to Login instead of terminating.
- Firebase messaging event registration is guarded so plugin initialization cannot block app startup.
- A final recovery page exists only if Login itself cannot be constructed.

## Phase 2 — Password eye
- `eye_open.png` and `eye_closed.png` are explicitly included as MAUI images.
- The eye button is always visible and has a dedicated 48x48 touch target.
- Tapping it toggles `PasswordEntry.IsPassword` and swaps the icon.
- The cursor is restored to the end of the password after toggling.

## Phase 3 — Visual Login redesign
- Purple premium background with layered curves and soft lighting.
- Himo logo and welcome header.
- Large rounded login card.
- Refined input fields and password control.
- Prominent gradient login button.
- Password recovery and account creation controls retained.
- Registration/verification fields retain their existing names and event handlers.

## Scope protection
`LoginPage.xaml.cs` keeps the existing account/API operations. No API endpoints, database logic, authentication contract, or navigation workflow was intentionally changed.

## Verification performed in this environment
- Login XAML parsed successfully as XML.
- App XAML parsed successfully as XML.
- All Login XAML event handlers were found in `LoginPage.xaml.cs`.
- Password eye resources exist and are explicitly listed in `Himo.csproj`.
- C# brace balance checked for `App.xaml.cs` and `LoginPage.xaml.cs`.
- Android/.NET build was not available in this environment, so no claim of a completed Android compile is made here.
