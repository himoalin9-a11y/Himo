# Stage 4 — UI and dark-mode polish

This stage is intentionally limited to XAML presentation changes on top of stage3-redone.

## Changes
- Replaced hard-coded semantic colors in ChatPage with the existing DynamicResource theme keys.
- Replaced hard-coded conversation-card/text colors in HomePage with the existing theme resources.
- No NuGet/package references were changed.
- No server-side source files were changed.
- No navigation or message logic was changed.

## Verification
- XAML parsed successfully.
- ChatPage and HomePage event handlers were checked against their code-behind.
- The archive was tested after packaging.
