using Himo.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace Himo;

public partial class AppShell : Shell
{
    static AppShell()
    {
        Routing.RegisterRoute("chat", typeof(ChatPage));
        Routing.RegisterRoute("CallPage", new CallPageRouteFactory());
        Routing.RegisterRoute("settings", new SettingsPageRouteFactory());
        Routing.RegisterRoute("profile", new ProfilePageRouteFactory());
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

internal sealed class CallPageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return services.GetRequiredService<CallPage>();
    }

    public override Element GetOrCreate(IServiceProvider services) =>
        services.GetRequiredService<CallPage>();
}

internal sealed class SettingsPageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return services.GetRequiredService<SettingsPage>();
    }

    public override Element GetOrCreate(IServiceProvider services) =>
        services.GetRequiredService<SettingsPage>();
}

internal sealed class ProfilePageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return services.GetRequiredService<ProfilePage>();
    }

    public override Element GetOrCreate(IServiceProvider services) =>
        services.GetRequiredService<ProfilePage>();
}
