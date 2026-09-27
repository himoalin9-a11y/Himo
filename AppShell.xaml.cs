using Himo.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace Himo;

public partial class AppShell : Shell
{
    static AppShell()
    {
        // The pages are registered as singletons in MauiProgram. Registering the
        // route by Type alone lets Shell create a fresh page for every navigation,
        // which defeats that singleton registration and forces InitializeComponent
        // to rebuild the entire visual tree each time.
        Routing.RegisterRoute("chat", new SingletonRouteFactory<ChatPage>());
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
