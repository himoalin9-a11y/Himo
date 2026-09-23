Himo v239 – Conversations UI Recovery

Restored the conversations/home screen XAML to the last known working v237 version.
The v238 conversation redesign introduced invalid Border StrokeThickness values using four-component values, which can prevent the XAML page from loading correctly.
No messaging, authentication, networking, database, or chat logic was changed.
