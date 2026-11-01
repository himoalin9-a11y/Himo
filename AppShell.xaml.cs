using Himo.Views;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Himo;

public partial class AppShell : Shell
{
    private static readonly SettingsPageRouteFactory SettingsFactory = new();
    private static readonly ProfilePageRouteFactory ProfileFactory = new();
    private static readonly QrScannerPageRouteFactory QrScannerFactory = new();
    private static readonly ShareAccountPageRouteFactory ShareAccountFactory = new();
    private readonly IServiceProvider _services;
    private int _prewarmState;

    static AppShell()
    {
        Routing.RegisterRoute("chat", new ChatPageRouteFactory());
        Routing.RegisterRoute("CallPage", new CallPageRouteFactory());
        Routing.RegisterRoute("settings", SettingsFactory);
        Routing.RegisterRoute("profile", ProfileFactory);
        Routing.RegisterRoute("qr-scanner", QrScannerFactory);
        Routing.RegisterRoute("share-account", ShareAccountFactory);
        Routing.RegisterRoute(nameof(QrScannerPage), QrScannerFactory);
        Routing.RegisterRoute(nameof(ShareAccountPage), ShareAccountFactory);
        Routing.RegisterRoute("search", typeof(SearchPage));
        Routing.RegisterRoute("new-conversation", typeof(NewConversationPage));
        Routing.RegisterRoute("group-invite", new GroupInvitePreviewPageRouteFactory());
        Routing.RegisterRoute("CreateGroupPage", new CreateGroupPageRouteFactory());
        Routing.RegisterRoute("icons-appearance", typeof(IconsAppearanceSettingsPage));
        Routing.RegisterRoute("icon-selection", typeof(IconSelectionSettingsPage));
        Routing.RegisterRoute("appearance-settings", typeof(AppearanceSettingsPage));
        Routing.RegisterRoute("default-interface-settings", typeof(DefaultInterfacesSettingsPage));
        Routing.RegisterRoute("system-icons-settings", typeof(SystemIconsSettingsPage));
        Routing.RegisterRoute("glass-icons-settings", typeof(GlassIconsSettingsPage));
        Routing.RegisterRoute("professional-icons-settings", typeof(ProfessionalIconsSettingsPage));
    }

    public AppShell()
    {
        InitializeComponent();
        Navigated += OnShellNavigated;
        _services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");

        // A new Shell can be created after the app-lock/session recovery flow.
        // Do not reuse page instances that belonged to the previous Shell.
        SettingsFactory.ResetCache();
        ProfileFactory.ResetCache();

        var homePage = _services.GetRequiredService<HomePage>();
        HomeContent.Content = homePage;
        AppLocalization.AttachToPage(homePage);
#if ANDROID
        // A verified App Link can launch the activity before Shell exists. Process
        // the pending URI now that Shell routes and the home page are ready.
        global::Himo.MainActivity.TryNavigateToPendingAppLink();
#endif
        // Also cover the sign-in and app-unlock flows that enter AppShell without
        // going through the welcome-page timer.
        _ = PrewarmAccountPagesAfterFirstFrameAsync();
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var page = CurrentPage;
        if (page is null)
            return;

        AppLocalization.AttachToPage(page);
        if (page is ChatPage or CallPage)
            return;

        // ChatPage/CallPage have their own optimized, state-aware styling.
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(page, batchSize: 48);
    }

    /// <summary>
    /// Build the two expensive primary pages while the two-second welcome screen
    /// is visible, then keep them warm for the first navigation as well as later visits.
    /// Must be called on the UI thread.
    /// </summary>
    internal void PrewarmAccountPages()
    {
        // Keep the public entry point for the existing splash/session flow, but do
        // not construct both heavyweight pages in one UI-thread turn.
        if (Interlocked.CompareExchange(ref _prewarmState, 1, 0) != 0)
            return;

        _ = PrewarmPagesInSeparateTurnsAsync();
    }

    private async Task PrewarmPagesInSeparateTurnsAsync()
    {
        try
        {
            // Each page constructor builds a large XAML tree. Separating the work
            // gives Android a chance to render between pages instead of producing
            // one long frame stall that can be felt during navigation.
            await MainThread.InvokeOnMainThreadAsync(() => SettingsFactory.Preload(_services));
            await Task.Delay(350).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => ProfileFactory.Preload(_services));
            Volatile.Write(ref _prewarmState, 2);
        }
        catch (Exception ex)
        {
            // Prewarming is only an optimization; normal lazy navigation remains available.
            Volatile.Write(ref _prewarmState, 0);
            System.Diagnostics.Debug.WriteLine($"[Himo PageCache] Staggered prewarming skipped: {ex.Message}");
        }
    }

    private async Task PrewarmAccountPagesAfterFirstFrameAsync()
    {
        try
        {
            // Wait longer than one frame burst and warm only one page per UI turn.
            // If the user opens a page before it is warmed, normal navigation still works.
            await Task.Delay(1400).ConfigureAwait(false);
            if (Volatile.Read(ref _prewarmState) == 2 || Volatile.Read(ref _prewarmState) == 1)
                return;
            await MainThread.InvokeOnMainThreadAsync(PrewarmAccountPages);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo PageCache] Idle prewarm skipped: {ex.Message}");
        }
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

/// <summary>
/// Keeps one detached instance ready for fast navigation. Never pushes an instance
/// that is already in Shell's stack; in that unusual case a temporary page is created.
/// </summary>
internal sealed class SettingsPageRouteFactory : RouteFactory
{
    private SettingsPage? _cachedPage;

    public SettingsPage Preload(IServiceProvider services) => GetReusableOrCreate(services);

    public void ResetCache() => _cachedPage = null;

    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return GetReusableOrCreate(services);
    }

    public override Element GetOrCreate(IServiceProvider services) => GetReusableOrCreate(services);

    private SettingsPage GetReusableOrCreate(IServiceProvider services)
    {
        if (_cachedPage is not null && !IsInNavigationStack(_cachedPage))
            return _cachedPage;

        var page = services.GetRequiredService<SettingsPage>();
        if (_cachedPage is null)
            _cachedPage = page;
        return page;
    }

    private static bool IsInNavigationStack(Page page) =>
        Shell.Current?.Navigation?.NavigationStack.Any(item => ReferenceEquals(item, page)) == true;
}

internal sealed class ProfilePageRouteFactory : RouteFactory
{
    private ProfilePage? _cachedPage;

    public ProfilePage Preload(IServiceProvider services) => GetReusableOrCreate(services);

    public void ResetCache() => _cachedPage = null;

    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return GetReusableOrCreate(services);
    }

    public override Element GetOrCreate(IServiceProvider services) => GetReusableOrCreate(services);

    private ProfilePage GetReusableOrCreate(IServiceProvider services)
    {
        if (_cachedPage is not null && !IsInNavigationStack(_cachedPage))
            return _cachedPage;

        var page = services.GetRequiredService<ProfilePage>();
        if (_cachedPage is null)
            _cachedPage = page;
        return page;
    }

    private static bool IsInNavigationStack(Page page) =>
        Shell.Current?.Navigation?.NavigationStack.Any(item => ReferenceEquals(item, page)) == true;
}

internal sealed class CreateGroupPageRouteFactory : RouteFactory
{
    private static CreateGroupPage CreatePage(IServiceProvider services) =>
        new CreateGroupPage(
            services.GetRequiredService<HimoApiClient>(),
            services.GetRequiredService<HomeViewModel>());

    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق.");
        return CreatePage(services);
    }

    public override Element GetOrCreate(IServiceProvider services) => CreatePage(services);
}

internal sealed class GroupInvitePreviewPageRouteFactory : RouteFactory
{
    private static GroupInvitePreviewPage CreatePage(IServiceProvider services) =>
        new GroupInvitePreviewPage(
            services.GetRequiredService<HimoApiClient>(),
            services.GetRequiredService<HomeViewModel>());

    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق عند فتح دعوة المجموعة.");
        return CreatePage(services);
    }

    public override Element GetOrCreate(IServiceProvider services) => CreatePage(services);
}


internal sealed class QrScannerPageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق عند فتح ماسح QR.");
        return services.GetRequiredService<QrScannerPage>();
    }

    public override Element GetOrCreate(IServiceProvider services) =>
        services.GetRequiredService<QrScannerPage>();
}

internal sealed class ShareAccountPageRouteFactory : RouteFactory
{
    public override Element GetOrCreate()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("تعذر الوصول إلى خدمات التطبيق عند فتح مشاركة الحساب.");
        return services.GetRequiredService<ShareAccountPage>();
    }

    public override Element GetOrCreate(IServiceProvider services) =>
        services.GetRequiredService<ShareAccountPage>();
}
