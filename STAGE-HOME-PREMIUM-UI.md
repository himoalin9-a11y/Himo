# Himo HomePage Premium UI

Implemented the HomePage visual redesign only.

- Dark premium purple RTL layout matching the supplied reference.
- Header, Himo brand treatment, profile/search/menu controls.
- Dark search bar and segmented conversation tabs.
- Flat conversation list with avatar initials, online indicator, preview, time, unread badge and chevron.
- Floating new-conversation button keeps `NewConversationClicked`.
- Four-item bottom navigation is visual only except existing Home/search/new-conversation interactions; no new navigation routes were introduced.
- Existing x:Name fields and event handlers are preserved.
- No NuGet packages or API/authentication/ChatService/HimoApiClient changes.
- Existing `HomePage.xaml.cs` is unchanged.
- Reused existing image/SVG assets, including `login_waves.svg`.

Validation in this environment: HomePage.xaml parses as valid XML. Full .NET MAUI build must be run on the user's Windows/.NET environment because `dotnet` is not installed in this container.
