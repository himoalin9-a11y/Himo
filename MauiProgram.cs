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
#if ANDROID
    private static readonly object FirebaseStartupGate = new();
    private static TaskCompletionSource<bool> _firebaseStartupCompletion = NewFirebaseCompletion();
    private static int _firebaseStartupScheduled;
    private static int _firebaseStartupCompleted;

    private static TaskCompletionSource<bool> NewFirebaseCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task<bool> WaitForFirebaseStartupAsync(TimeSpan timeout)
    {
        Task<bool> task;
        lock (FirebaseStartupGate)
            task = _firebaseStartupCompletion.Task;
        try { return await task.WaitAsync(timeout).ConfigureAwait(false); }
        catch { return false; }
    }

    private static void InitializeFirebaseOnCreate(global::Android.App.Activity activity)
    {
        TaskCompletionSource<bool> completion;
        lock (FirebaseStartupGate)
        {
            if (_firebaseStartupCompleted != 0)
                return;
            if (_firebaseStartupScheduled != 0)
                return;
            if (_firebaseStartupCompletion.Task.IsCompleted)
                _firebaseStartupCompletion = NewFirebaseCompletion();
            completion = _firebaseStartupCompletion;
            _firebaseStartupScheduled = 1;
        }

        var initialized = false;
        try
        {
            if (activity.IsFinishing || activity.IsDestroyed)
                return;

            // Plugin.Firebase documents initialization in Android OnCreate. Keep
            // it before MainActivity handles launch intents, rather than delaying
            // it until after the first frame where it can cause a blank/white pause.
            CrossFirebase.Initialize(activity, () => activity);
            Volatile.Write(ref _firebaseStartupCompleted, 1);
            initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Startup] Firebase initialization failed: {ex}");
        }
        finally
        {
            lock (FirebaseStartupGate)
            {
                _firebaseStartupScheduled = 0;
                completion.TrySetResult(initialized);
            }
        }
    }

#endif

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement();

#if ANDROID
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Views.CameraPreview, Platforms.Android.CameraPreviewHandler>();
        });

        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddAndroid(android => android.OnCreate((activity, _) =>
            {
                FirebaseCloudMessagingImplementation.ChannelId = Platforms.Android.Services.NotificationService.MessageChannelId;
                InitializeFirebaseOnCreate(activity);
            }));
        });
#endif

        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<AppLockService>();
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddSingleton<CallHistoryService>();
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
        builder.Services.AddSingleton<PushNotificationManager>();
#else
        builder.Services.AddSingleton<INotificationService, NoOpNotificationService>();
#endif
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<Views.HomePage>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddTransient<Views.ChatPage>();
        // ContentPage instances have navigation state and must not be reused as singletons.
        builder.Services.AddTransient<Views.SettingsPage>();
        builder.Services.AddTransient<Views.AccountManagementPage>();
        builder.Services.AddTransient<Views.AccountUsernamePage>();
        builder.Services.AddTransient<Views.AccountPhonePage>();
        builder.Services.AddTransient<Views.AccountLoginDetailsPage>();
        builder.Services.AddTransient<Views.AccountPasswordPage>();
        builder.Services.AddTransient<Views.AddAccountPage>();
        builder.Services.AddTransient<Views.ProfilePage>();
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
    public Task ShowIncomingCallAsync(string callerName, string conversationId, string mode, string callId) => Task.CompletedTask;
    public Task ClearIncomingCallAsync(string conversationId, string callId, bool suppressFurtherRinging = true) => Task.CompletedTask;
    public bool IsIncomingCallSuppressed(string callId) => false;
    public Task ClearConversationAsync(string conversationId) => Task.CompletedTask;
    public Task ClearAllAsync() => Task.CompletedTask;
}
