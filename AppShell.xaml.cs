using Himo.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace Himo;

public partial class AppShell : Shell
{
    static AppShell()
    {
        Routing.RegisterRoute("chat", new ChatPageRouteFactory());
        Routing.RegisterRoute("CallPage", new CallPageRouteFactory());
        Routing.RegisterRoute("settings", new SettingsPageRouteFactory());
        Routing.RegisterRoute("profile", new ProfilePageRouteFactory());
        Routing.RegisterRoute("search", typeof(SearchPage));
        Routing.RegisterRoute("CreateGroupPage", new CreateGroupPageRouteFactory());
    }

    public AppShell()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");

        HomeContent.Content = services.GetRequiredService<HomePage>();
    }
}

internal sealed class ChatPageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق عند إنشاء واجهة المحادثة.");
        var page = services.GetRequiredService<ChatPage>();
        System.Diagnostics.Debug.WriteLine("[Himo ChatNav] ChatPage was created through the app service provider.");
        return page;
    }

    public override Element GetOrCreate(IServiceProvider services)
    {
        var page = services.GetRequiredService<ChatPage>();
        System.Diagnostics.Debug.WriteLine("[Himo ChatNav] ChatPage was resolved from Shell route services.");
        return page;
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

internal sealed class CreateGroupPageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return services.GetRequiredService<CreateGroupPage>();
    }

    public override Element GetOrCreate(IServiceProvider services) =>
        services.GetRequiredService<CreateGroupPage>();
}
