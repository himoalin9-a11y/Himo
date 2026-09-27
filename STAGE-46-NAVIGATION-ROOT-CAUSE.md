# Stage 46 — Navigation Root Cause Fix

## Root cause identified from the application source
The navigation routes for Chat, Settings, and Profile were registered as transient MAUI pages:
- every navigation could construct a new page instance;
- construction runs on the UI thread and includes XAML initialization;
- ChatPage also wires multiple realtime handlers and owns a large message CollectionView template;
- ProfilePage construction could also instantiate ProfileService and perform its synchronous local profile load.

This work does **not** treat network calls as the navigation blocker: the affected pages start their remote work after `OnAppearing` and do not await it before returning from the lifecycle callback.

## Targeted fix
- ChatPage, SettingsPage, and ProfilePage are now singleton page instances in MAUI DI, so repeated navigation reuses the already-created visual tree instead of rebuilding it.
- ChatPage now resets conversation-specific transient UI state when Shell reuses the cached page for another conversation.
- SearchPage remains transient because it is lightweight and search state is intentionally fresh.
- No package or project-file changes.
