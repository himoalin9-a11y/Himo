using System.Reflection;

namespace Himo.Services;

/// <summary>
/// Keeps page code independent from the generated MAUI App partial type while
/// preserving the existing App lifecycle behavior.
/// </summary>
public static class AppRuntimeBridge
{
    public static bool IsDarkModeEnabled
        => Preferences.Default.Get("himo_dark_mode", false);

    public static void SetDarkMode(bool enabled)
    {
        Preferences.Default.Set("himo_dark_mode", enabled);
        if (Application.Current is not null)
            Application.Current.UserAppTheme = enabled ? AppTheme.Dark : AppTheme.Light;
    }

    public static void MarkSessionUnlocked()
        => InvokeInstanceMethod("MarkSessionUnlocked");

#if ANDROID
    public static Task RegisterCurrentPushTokenAsync()
        => InvokeTaskMethod("RegisterCurrentPushTokenAsync");
#endif

    private static void InvokeInstanceMethod(string methodName)
    {
        var app = Application.Current;
        if (app is null) return;

        try
        {
            var method = app.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            method?.Invoke(app, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App runtime call '{methodName}' failed: {ex}");
        }
    }

#if ANDROID
    private static Task InvokeTaskMethod(string methodName)
    {
        var app = Application.Current;
        if (app is null) return Task.CompletedTask;

        try
        {
            var method = app.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            return method?.Invoke(app, null) as Task ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App runtime call '{methodName}' failed: {ex}");
            return Task.CompletedTask;
        }
    }
#endif
}
