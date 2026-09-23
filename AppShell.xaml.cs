using Himo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Himo;

public partial class AppShell : Shell
{
    static AppShell()
    {
        // Shell routes are global. Register them once so logging out and
        // signing back in cannot try to register the same route twice.
        Routing.RegisterRoute("chat", typeof(ChatPage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));
        Routing.RegisterRoute("profile", typeof(ProfilePage));
        Routing.RegisterRoute("search", typeof(SearchPage));
    }

    public AppShell()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");

        HomeContent.Content = services.GetRequiredService<HomePage>();
    }
}
