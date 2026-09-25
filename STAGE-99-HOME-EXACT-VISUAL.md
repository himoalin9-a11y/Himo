# Stage 99 — Himo Home Exact Visual

This stage updates only the Home screen XAML to closely match the approved Himo Home mockup:
- Purple gradient hero header with Himo branding and settings action.
- Arabic greeting and compact decorative visual treatment.
- Floating rounded search panel overlapping the header.
- Conversations section with rounded cards, avatars/initials, timestamps and unread badges.
- Four-item bottom navigation: Home, Search, New Conversation, Profile.
- Center floating New Conversation button.

Preserved:
- HomePage.xaml.cs unchanged.
- HomeViewModel unchanged.
- ChatService, HimoApiClient, notifications, polling, refresh and navigation logic unchanged.
- Server settings remain outside the Home UI.

The project must be rebuilt in Visual Studio on the development machine because this environment does not contain the .NET MAUI Android SDK.
