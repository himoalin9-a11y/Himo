using Himo.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace Himo;

public partial class AppShell : Shell
{
    static AppShell()
    {
        // ChatPage is transient, so let Shell resolve a fresh page through the
        // MAUI DI container for every navigation. This avoids reusing a page whose
        // Handler/MauiContext may already have been disposed.
        Routing.RegisterRoute("chat", typeof(ChatPage));
        Routing.RegisterRoute("settings", new SingletonRouteFactory<SettingsPage>());
        Routing.RegisterRoute("profile", new SingletonRouteFactory<ProfilePage>());
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

/// <summary>
/// Makes Shell navigation resolve a page from MAUI DI instead of constructing a
/// new instance from the registered page type on every navigation.
/// </summary>
internal sealed class SingletonRouteFactory<TPage> : RouteFactory
    where TPage : Element
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");

        return services.GetRequiredService<TPage>();
    }

    public override Element GetOrCreate(IServiceProvider services)
    {
        return services.GetRequiredService<TPage>();
    }

}
