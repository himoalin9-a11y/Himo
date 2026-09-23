Himo v248 – Settings API UI cleanup

Fixed compile errors caused by the SettingsPage code-behind still referencing API configuration controls that were removed from the redesigned SettingsPage UI:
- ApiUrlEntry
- ApiStatusLabel
- TestApiButton

Removed the obsolete API save/test handlers and their state fields from SettingsPage.xaml.cs.
The HimoApiClient constructor dependency is retained for DI compatibility, but the server/API configuration remains hidden from the user-facing Settings UI as requested.
No authentication, chat, database, notifications, or networking behavior was changed.
