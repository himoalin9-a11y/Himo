Himo v249 - Full SettingsPage compile fix

Fixed the remaining SettingsPage code-behind mismatch after the server settings UI was removed.

- Restored the private HimoApiClient _api field.
- Assigned the constructor api argument to _api.
- Removed the obsolete ApiUrlEntry reference because the server URL editor is intentionally hidden from the user-facing Settings UI.
- Preserved all account, logout, delete-account, notification, app-lock, and authentication behavior.
- Audited the project for the removed ApiUrlEntry / ApiStatusLabel / TestApiButton names; no remaining references exist in the source.
- Removed build/cache directories from the archive.
