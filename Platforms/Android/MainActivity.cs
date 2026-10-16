using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Plugin.Firebase.CloudMessaging;

namespace Himo;

[global::Android.App.Activity(
    Name = "com.companyname.himo.LauncherIconDefault",
    Theme = "@style/Maui.SplashTheme",
    // This stable native component is never toggled; icon aliases target it, and Visual Studio can always launch it.
    // Keep MainActivity non-launchable directly; visible launcher icons remain activity-alias entries.
    MainLauncher = false,
    Exported = true,
    ResizeableActivity = true,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTask,
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.ScreenSize
        | global::Android.Content.PM.ConfigChanges.Orientation
        | global::Android.Content.PM.ConfigChanges.UiMode
        | global::Android.Content.PM.ConfigChanges.ScreenLayout
        | global::Android.Content.PM.ConfigChanges.SmallestScreenSize
        | global::Android.Content.PM.ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static string? _pendingConversationId;
    private static string? _pendingCallConversationId;
    private static string? _pendingCallMode;
    private static string? _pendingCallId;
    private static int _navigationInProgress;
    private static int _callNavigationInProgress;
    private static int _isAppForeground;
    public static bool IsAppForeground => Volatile.Read(ref _isAppForeground) != 0;
    // A cold-start notification and the SignalR Invite may arrive for the same
    // call. Keep the last successfully opened CallId to avoid stacking two pages.
    private static string? _lastNavigatedCallKey;
    private const string PendingConversationPreferenceKey = "himo_pending_conversation_id";
    private const string PendingCallConversationPreferenceKey = "himo_pending_call_conversation_id";
    private const string PendingCallModePreferenceKey = "himo_pending_call_mode";
    private const string PendingCallIdPreferenceKey = "himo_pending_call_id";
    private const string FullScreenIntentPromptPreferenceKey = "himo_full_screen_intent_prompted_v1";
    private static int _fullScreenIntentPromptInProgress;

    public static string? PendingConversationId => _pendingConversationId;

    public static void ClearPendingConversation()
    {
        _pendingConversationId = null;
        Preferences.Default.Remove(PendingConversationPreferenceKey);
    }

    public static void SetPendingConversation(string? conversationId)
    {
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            _pendingConversationId = conversationId;
            Preferences.Default.Set(PendingConversationPreferenceKey, conversationId);
        }
    }

    public static void SetPendingCall(string conversationId, string mode, string? callId = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;
        // A call intent takes precedence over a stale chat-notification intent;
        // running both navigation retries was one source of stacked call screens.
        ClearPendingConversation();
        _pendingCallConversationId = conversationId;
        _pendingCallMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        _pendingCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D")
            : null;

        Preferences.Default.Set(PendingCallConversationPreferenceKey, conversationId);
        Preferences.Default.Set(PendingCallModePreferenceKey, _pendingCallMode);
        if (_pendingCallId is not null)
            Preferences.Default.Set(PendingCallIdPreferenceKey, _pendingCallId);
        else
            Preferences.Default.Remove(PendingCallIdPreferenceKey);
    }

    public static void ClearPendingCall()
    {
        _pendingCallConversationId = null;
        _pendingCallMode = null;
        _pendingCallId = null;
        Preferences.Default.Remove(PendingCallConversationPreferenceKey);
        Preferences.Default.Remove(PendingCallModePreferenceKey);
        Preferences.Default.Remove(PendingCallIdPreferenceKey);
    }

    public static void ClearPendingCallIfMatches(string conversationId, string? callId)
    {
        RestorePendingCall();
        if (string.IsNullOrWhiteSpace(_pendingCallConversationId) ||
            !string.Equals(_pendingCallConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.IsNullOrWhiteSpace(callId) && !string.IsNullOrWhiteSpace(_pendingCallId) &&
            !string.Equals(_pendingCallId, callId, StringComparison.OrdinalIgnoreCase))
            return;
        ClearPendingCall();
    }

    protected override void OnResume()
    {
        base.OnResume();
        Volatile.Write(ref _isAppForeground, 1);

        // Do not show an incoming-call UI over other apps. Route the saved invite only after Himo opens.
        RestorePendingCall();
        TryNavigateToPendingCall();
        SchedulePendingCallNavigation();

        try
        {
            var activeCallPage = Shell.Current?.Navigation.NavigationStack
                .OfType<Himo.Views.CallPage>().LastOrDefault();
            if (activeCallPage is not null)
                _ = activeCallPage.ResumeVideoRenderingForForegroundAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Resume video rendering skipped: {ex.Message}");
        }
    }

    protected override void OnPause()
    {
        Volatile.Write(ref _isAppForeground, 0);
        base.OnPause();
    }

    protected override void OnStop()
    {
        try
        {
            var callPage = Shell.Current?.Navigation.NavigationStack
                .OfType<Himo.Views.CallPage>().LastOrDefault();
            if (callPage is not null)
            {
                callPage.EnsureBackgroundRingtoneIfIncoming();
                _ = callPage.PauseVideoForBackgroundAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Background video pause skipped: {ex.Message}");
        }
        base.OnStop();

        // Complete launcher icon changes only after Himo has left the foreground.
        // This prevents disabling the launch alias currently associated with the visible task.
        Himo.Services.HimoLauncherIconService.FinalizePendingAliasChanges();
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try { FirebaseCloudMessagingImplementation.OnNewIntent(Intent); } catch { }
        CreateNotificationChannel();

        CaptureNotificationIntent(Intent);
        RestorePendingConversation();
        RestorePendingCall();
        TryNavigateToPendingCall();
        TryNavigateToPendingConversation();
        SchedulePendingCallNavigation();
        SchedulePendingConversationNavigation();
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null) return;
        Intent = intent;
        try { FirebaseCloudMessagingImplementation.OnNewIntent(intent); } catch { }

        CaptureNotificationIntent(intent);
        RestorePendingConversation();
        RestorePendingCall();
        TryNavigateToPendingCall();
        TryNavigateToPendingConversation();
    }

    public static void TryNavigateToPendingCall()
    {
        RestorePendingCall();
        var conversationId = _pendingCallConversationId;
        var mode = _pendingCallMode ?? "audio";
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Interlocked.Exchange(ref _callNavigationInProgress, 1) != 0) return;
            try
            {
                var shell = Shell.Current;
                if (shell is null) return;
                conversationId = _pendingCallConversationId;
                mode = _pendingCallMode ?? "audio";
                var callId = _pendingCallId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(conversationId)) return;
                var callKey = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
                    ? $"{conversationId}:{parsedCallId:D}"
                    : string.Empty;

                // If an incoming-call page for this call is already on the Shell stack,
                // don't push another transient page when FCM and SignalR race.
                var existingCallPage = shell.Navigation.NavigationStack
                    .OfType<Himo.Views.CallPage>()
                    .FirstOrDefault(page => page.IsForCall(conversationId, callId));
                if (existingCallPage is not null)
                {
                    if (callKey.Length > 0) _lastNavigatedCallKey = callKey;
                    ClearPendingCall();
                    return;
                }

                // FCM may launch the call page first, then the same Invite reaches
                // SignalR after startup. Do not put a second (stale) answer screen
                // on top of the active call page.
                if (callKey.Length > 0 && string.Equals(_lastNavigatedCallKey, callKey, StringComparison.OrdinalIgnoreCase))
                {
                    ClearPendingCall();
                    return;
                }

                var callIdQuery = string.IsNullOrWhiteSpace(callId)
                    ? string.Empty
                    : $"&callId={Uri.EscapeDataString(callId)}";
                await shell.GoToAsync($"CallPage?id={Uri.EscapeDataString(conversationId)}&mode={Uri.EscapeDataString(mode)}&incoming=true{callIdQuery}");
                global::Himo.Platforms.Android.Services.NotificationService.StopBackgroundIncomingCallRingtone();
                if (callKey.Length > 0) _lastNavigatedCallKey = callKey;
                ClearPendingCall();
            }
            catch
            {
                // Shell may still be initializing; startup retry handles it.
            }
            finally
            {
                Volatile.Write(ref _callNavigationInProgress, 0);
            }
        });
    }

    public static void TryNavigateToPendingConversation()
    {
        var conversationId = _pendingConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            conversationId = Preferences.Default.Get(PendingConversationPreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(conversationId))
                _pendingConversationId = conversationId;
        }
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Interlocked.Exchange(ref _navigationInProgress, 1) != 0) return;

            try
            {
                // Read again on the UI thread so a newer notification intent wins.
                conversationId = _pendingConversationId;
                if (string.IsNullOrWhiteSpace(conversationId)) return;

                var shell = Shell.Current;
                if (shell is null) return;

                await shell.GoToAsync($"chat?id={Uri.EscapeDataString(conversationId)}");
                if (string.Equals(_pendingConversationId, conversationId, StringComparison.Ordinal))
                    ClearPendingConversation();
            }
            catch
            {
                // The shell may still be initializing; App will retry after startup.
            }
            finally
            {
                Volatile.Write(ref _navigationInProgress, 0);
            }
        });
    }


    private static void RestorePendingCall()
    {
        if (string.IsNullOrWhiteSpace(_pendingCallConversationId))
            _pendingCallConversationId = Preferences.Default.Get(PendingCallConversationPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(_pendingCallMode))
            _pendingCallMode = Preferences.Default.Get(PendingCallModePreferenceKey, "audio");
        if (string.IsNullOrWhiteSpace(_pendingCallId))
            _pendingCallId = Preferences.Default.Get(PendingCallIdPreferenceKey, string.Empty);
    }

    private static void SchedulePendingCallNavigation()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                RestorePendingCall();
                if (string.IsNullOrWhiteSpace(_pendingCallConversationId)) break;
                await Task.Delay(attempt == 0 ? 350 : 500);
                TryNavigateToPendingCall();
            }
        });
    }

    private static void RestorePendingConversation()
    {
        if (string.IsNullOrWhiteSpace(_pendingConversationId))
        {
            var saved = Preferences.Default.Get(PendingConversationPreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
                _pendingConversationId = saved;
        }
    }

    private static void SchedulePendingConversationNavigation()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                RestorePendingConversation();
                if (string.IsNullOrWhiteSpace(_pendingConversationId)) break;
                await Task.Delay(attempt == 0 ? 350 : 500);
                TryNavigateToPendingConversation();
            }
        });
    }

    private static void CaptureNotificationIntent(global::Android.Content.Intent? intent)
    {
        if (intent is null) return;

        var conversationId = intent.GetStringExtra("conversation_id");
        var callType = intent.GetStringExtra("call_type");
        var callMode = intent.GetStringExtra("call_mode");
        var callId = intent.GetStringExtra("call_id");
        if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingCall(conversationId, callMode ?? "audio", callId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingConversation(conversationId);
            return;
        }

        var found = FindNotificationData(intent.Extras);
        if (found.CallConversationId is not null)
        {
            SetPendingCall(found.CallConversationId, found.CallMode ?? "audio", found.CallId);
            return;
        }
        if (found.ConversationId is not null)
            SetPendingConversation(found.ConversationId);
    }

    private static (string? ConversationId, string? CallConversationId, string? CallMode, string? CallId) FindNotificationData(global::Android.OS.Bundle? bundle)
    {
        if (bundle is null) return (null, null, null, null);
        var conversationId = bundle.GetString("conversation_id");
        var callType = bundle.GetString("call_type");
        var callMode = bundle.GetString("call_mode");
        var callId = bundle.GetString("call_id");
        if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conversationId))
            return (conversationId, conversationId, callMode, callId);
        if (!string.IsNullOrWhiteSpace(conversationId)) return (conversationId, null, null, null);

        foreach (var key in bundle.KeySet() ?? new global::System.Collections.Generic.HashSet<string>())
        {
            try
            {
                if (bundle.Get(key) is global::Android.OS.Bundle nested)
                {
                    var found = FindNotificationData(nested);
                    if (found.CallConversationId is not null || found.ConversationId is not null) return found;
                }
            }
            catch { }
        }
        return (null, null, null, null);
    }

    private void PromptForFullScreenIntentAccessIfNeeded()
    {
        // Android 14+ makes full-screen call UI a special app-access setting. Merely
        // declaring USE_FULL_SCREEN_INTENT in the manifest does not guarantee access.
        if (!OperatingSystem.IsAndroidVersionAtLeast(34)) return;

        try
        {
            var manager = GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;
            if (manager is null || manager.CanUseFullScreenIntent()) return;
            if (global::Microsoft.Maui.Storage.Preferences.Default.Get(
                    FullScreenIntentPromptPreferenceKey, false)) return;
            if (Interlocked.Exchange(ref _fullScreenIntentPromptInProgress, 1) != 0) return;

            global::Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Let MAUI finish attaching the activity content before presenting
                    // a system-access explanation over the splash/startup transition.
                    await Task.Delay(650);
                    if (IsFinishing || IsDestroyed) return;

                    var dialog = new global::Android.App.AlertDialog.Builder(this)
                        .SetTitle("السماح بواجهة المكالمات")
                        .SetMessage("لكي تظهر واجهة المكالمة الواردة بملء الشاشة عندما يكون Himo بالخلفية أو مغلقًا، فعّل السماح بعرض الإشعارات بملء الشاشة من إعدادات Himo. إذا لم تفعّله، فقد تظهر المكالمة كإشعار فقط.")
                        .SetPositiveButton("فتح الإعدادات", (sender, args) =>
                        {
                            try
                            {
                                var settingsIntent = new global::Android.Content.Intent(
                                    "android.settings.MANAGE_APP_USE_FULL_SCREEN_INTENT");
                                settingsIntent.SetData(global::Android.Net.Uri.Parse("package:" + PackageName));
                                StartActivity(settingsIntent);
                            }
                            catch (Exception settingsEx)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[Himo Call] Could not open full-screen access settings: {settingsEx.Message}");
                            }
                        })
                        .SetNegativeButton("لاحقًا", (sender, args) => { })
                        .SetCancelable(true)
                        .Create();

                    dialog.Show();
                    // Ask only once automatically; users can still enable it later in
                    // Android Settings > Apps > Special app access > Full-screen intents.
                    global::Microsoft.Maui.Storage.Preferences.Default.Set(
                        FullScreenIntentPromptPreferenceKey, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Call] Full-screen access prompt failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _fullScreenIntentPromptInProgress, 0);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo Call] Could not inspect full-screen access: {ex.Message}");
            Interlocked.Exchange(ref _fullScreenIntentPromptInProgress, 0);
        }
    }

    public static void OpenFullScreenIntentSettings()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(34)) return;
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new global::Android.Content.Intent(
                "android.settings.MANAGE_APP_USE_FULL_SCREEN_INTENT");
            intent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo Call] Could not open full-screen access settings: {ex.Message}");
        }
    }

    private void EnableIncomingCallWindowIfNeeded(global::Android.Content.Intent? intent)
    {
        var isIncomingCall = string.Equals(
            intent?.GetStringExtra("call_type"), "invite", StringComparison.OrdinalIgnoreCase);
        if (!isIncomingCall) return;
        try
        {
            Window?.AddFlags(
                global::Android.Views.WindowManagerFlags.ShowWhenLocked |
                global::Android.Views.WindowManagerFlags.TurnScreenOn);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Could not enable incoming-call window flags: {ex.Message}");
        }
    }

    public static void ClearIncomingCallWindowFlags()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            activity?.Window?.ClearFlags(
                global::Android.Views.WindowManagerFlags.ShowWhenLocked |
                global::Android.Views.WindowManagerFlags.TurnScreenOn);
        }
        catch { }
    }

    public static void ConfigureFirebaseMessagingChannel()
    {
        CreateNotificationChannel();
    }

    private static void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var context = global::Android.App.Application.Context;
        var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
            as global::Android.App.NotificationManager;
        if (manager is null) return;


        var channel = new global::Android.App.NotificationChannel(
            Platforms.Android.Services.NotificationService.MessageChannelId,
            "رسائل Himo",
            global::Android.App.NotificationImportance.High)
        {
            Description = "إشعارات الرسائل الجديدة في Himo"
        };
        channel.SetSound(
            global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification),
            null);
        manager.CreateNotificationChannel(channel);

        FirebaseCloudMessagingImplementation.ChannelId = Platforms.Android.Services.NotificationService.MessageChannelId;
    }

    public static bool IsNotificationPermissionGranted()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;

        var context = global::Android.App.Application.Context;
        return global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
            context,
            global::Android.Manifest.Permission.PostNotifications) == global::Android.Content.PM.Permission.Granted;
    }

    public static void OpenNotificationSettings()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;

            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not open Android notification settings: {ex}");
        }
    }

    public static void RequestNotificationPermissionIfNeeded()
    {
        // Do not ask Android for notification permission when the user has
        // explicitly disabled Himo notifications inside the app.
        if (!Preferences.Get("himo_notifications_enabled", true))
            return;

        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return;

        if (!IsNotificationPermissionGranted())
        {
            global::AndroidX.Core.App.ActivityCompat.RequestPermissions(
                activity,
                new[] { global::Android.Manifest.Permission.PostNotifications },
                7001);
        }
    }
}
