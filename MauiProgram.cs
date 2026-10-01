using CommunityToolkit.Maui;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.LifecycleEvents;
#if ANDROID
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.Core.Platforms.Android;
#endif

namespace Himo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement();

#if ANDROID
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddAndroid(android => android.OnCreate((activity, _) =>
            {
                FirebaseCloudMessagingImplementation.ChannelId = "himo_messages";
                CrossFirebase.Initialize(activity, () => activity);
                MainActivity.ConfigureFirebaseMessagingChannel();
            }));
        });
#endif

                builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<AppLockService>();
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddSingleton<HimoApiClient>();
        builder.Services.AddSingleton<HimoRealtimeService>();
        builder.Services.AddSingleton<ICallService, CallService>();
        #if ANDROID
        builder.Services.AddSingleton<ICallMediaController, Platforms.Android.Services.AndroidCallMediaController>();
#else
        builder.Services.AddSingleton<ICallMediaController, NoOpCallMediaController>();
#endif
        #if ANDROID
        builder.Services.AddSingleton<IWebRtcMediaEngine, Platforms.Android.Services.AndroidWebRtcMediaEngine>();
        builder.Services.AddSingleton<WebRtcNegotiationCoordinator>();
#else
        builder.Services.AddSingleton<IWebRtcMediaEngine, NoOpWebRtcMediaEngine>();
#endif
#if ANDROID
        builder.Services.AddSingleton<INotificationService, Platforms.Android.Services.NotificationService>();
#else
        builder.Services.AddSingleton<INotificationService, NoOpNotificationService>();
#endif
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<Views.HomePage>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddSingleton<Views.ChatPage>();
        builder.Services.AddSingleton<Views.SettingsPage>();
        builder.Services.AddSingleton<Views.ProfilePage>();
        builder.Services.AddTransient<Views.SearchPage>();
        builder.Services.AddTransient<Views.CallPage>();
        builder.Services.AddTransient<Views.CreateGroupPage>();


        return builder.Build();
    }
}

internal sealed class NoOpNotificationService : INotificationService
{
    public bool IsEnabled => false;
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SetEnabledAsync(bool enabled) => Task.CompletedTask;
    public Task ShowMessageAsync(string senderName, string message, string conversationId) => Task.CompletedTask;
    public Task ClearConversationAsync(string conversationId) => Task.CompletedTask;
    public Task ClearAllAsync() => Task.CompletedTask;
}
