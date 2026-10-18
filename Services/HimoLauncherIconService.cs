using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>Applies a selected icon as the Android launcher icon using activity-alias entries.
/// Also stores the selected variant so the app can restore the selection in its settings UI.
/// </summary>
public static class HimoLauncherIconService
{
    private const string ActiveAliasPreference = "Himo.ActiveLauncherAlias";
    private const string PendingAliasCleanupPreference = "Himo.LauncherIconCleanupPending";
    private const string DefaultAliasName = "LauncherIconHomeDefault";
    public const string SelectedVariantPreference = "Himo.LauncherIconVariant";

    private static readonly IReadOnlyDictionary<string, string> AliasByFileName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
        ["himo_system_icon_original.png"] = "LauncherIconSystemOriginal",
        ["himo_system_icon_sapphire.png"] = "LauncherIconSystemSapphire",
        ["himo_system_icon_azure.png"] = "LauncherIconSystemAzure",
        ["himo_system_icon_cyan.png"] = "LauncherIconSystemCyan",
        ["himo_system_icon_turquoise.png"] = "LauncherIconSystemTurquoise",
        ["himo_system_icon_emerald.png"] = "LauncherIconSystemEmerald",
        ["himo_system_icon_green.png"] = "LauncherIconSystemGreen",
        ["himo_system_icon_lime.png"] = "LauncherIconSystemLime",
        ["himo_system_icon_gold.png"] = "LauncherIconSystemGold",
        ["himo_system_icon_amber.png"] = "LauncherIconSystemAmber",
        ["himo_system_icon_orange.png"] = "LauncherIconSystemOrange",
        ["himo_system_icon_ruby.png"] = "LauncherIconSystemRuby",
        ["himo_system_icon_rose.png"] = "LauncherIconSystemRose",
        ["himo_system_icon_magenta.png"] = "LauncherIconSystemMagenta",
        ["himo_system_icon_indigo.png"] = "LauncherIconSystemIndigo",
        ["himo_glass_icon_purple.png"] = "LauncherIconGlassPurple",
        ["himo_glass_icon_sapphire.png"] = "LauncherIconGlassSapphire",
        ["himo_glass_icon_rose.png"] = "LauncherIconGlassRose",
        ["himo_glass_icon_gold.png"] = "LauncherIconGlassGold",
        ["himo_glass_icon_turquoise.png"] = "LauncherIconGlassTurquoise",
        ["himo_glass_icon_ruby.png"] = "LauncherIconGlassRuby",
        ["himo_glass_icon_silver.png"] = "LauncherIconGlassSilver",
        ["himo_glass_icon_night.png"] = "LauncherIconGlassNight",
        ["himo_glass_icon_holographic.png"] = "LauncherIconGlassHolographic",
        ["himo_glass_icon_emerald.png"] = "LauncherIconGlassEmerald",
        ["himo_glass_icon_icecyan.png"] = "LauncherIconGlassIcecyan",
        ["himo_glass_icon_amber.png"] = "LauncherIconGlassAmber",
        ["himo_glass_icon_indigo.png"] = "LauncherIconGlassIndigo",
        ["himo_glass_icon_lime.png"] = "LauncherIconGlassLime",
        ["himo_glass_icon_magenta.png"] = "LauncherIconGlassMagenta",
        ["himo_professional_icon_01.png"] = "LauncherIconProfessional01",
        ["himo_professional_icon_02.png"] = "LauncherIconProfessional02",
        ["himo_professional_icon_03.png"] = "LauncherIconProfessional03",
        ["himo_professional_icon_04.png"] = "LauncherIconProfessional04",
        ["himo_professional_icon_05.png"] = "LauncherIconProfessional05",
        ["himo_professional_icon_06.png"] = "LauncherIconProfessional06",
        ["himo_professional_icon_07.png"] = "LauncherIconProfessional07",
        ["himo_professional_icon_08.png"] = "LauncherIconProfessional08",
        ["himo_professional_icon_09.png"] = "LauncherIconProfessional09",
        ["himo_professional_icon_10.png"] = "LauncherIconProfessional10",
        ["himo_professional_icon_11.png"] = "LauncherIconProfessional11",
        ["himo_professional_icon_12.png"] = "LauncherIconProfessional12",
        ["himo_professional_icon_13.png"] = "LauncherIconProfessional13",
        ["himo_professional_icon_14.png"] = "LauncherIconProfessional14",
        ["himo_professional_icon_15.png"] = "LauncherIconProfessional15",
        ["himo_professional_icon_16.png"] = "LauncherIconProfessional16",
        ["himo_professional_icon_17.png"] = "LauncherIconProfessional17",
        ["himo_professional_icon_18.png"] = "LauncherIconProfessional18",
        ["himo_professional_icon_19.png"] = "LauncherIconProfessional19",
        ["himo_professional_icon_20.png"] = "LauncherIconProfessional20",
        ["himo_professional_icon_21.png"] = "LauncherIconProfessional21",
        ["himo_professional_icon_22.png"] = "LauncherIconProfessional22",
        ["himo_professional_icon_23.png"] = "LauncherIconProfessional23",
        ["himo_professional_icon_24.png"] = "LauncherIconProfessional24",
        ["himo_professional_icon_25.png"] = "LauncherIconProfessional25",
        };

    /// <summary>
    /// Enables the selected launcher alias immediately, but deliberately does not disable
    /// the old launcher alias while Himo is in the foreground. The native Activity uses
    /// the stable component name LauncherIconDefault and is never included in cleanup.
    /// Launcher aliases are cleaned up when MainActivity reaches OnStop.
    /// </summary>
    public static bool TryApply(string imageFileName, out string message)
    {
        var fileName = Path.GetFileName(imageFileName ?? string.Empty);
        if (!AliasByFileName.TryGetValue(fileName, out var aliasName))
        {
            message = "هذا الملف ليس ضمن أيقونات التطبيق المدعومة.";
            return false;
        }

        // Retain the UI selection even when the OS launcher takes a moment to refresh.
        Preferences.Default.Set(SelectedVariantPreference, fileName);

#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var packageManager = context.PackageManager
                ?? throw new InvalidOperationException("تعذر الوصول إلى مدير حزم Android.");

            // Android 13+ must not expose the new alias to the launcher immediately. The
            // previous implementation enabled it now, then disabled the old aliases in OnStop;
            // some launchers can record the intermediate icon as a newly inserted home-screen
            // item. Save the requested alias and perform the entire switch atomically in OnStop.
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                Preferences.Default.Set(ActiveAliasPreference, aliasName);
                Preferences.Default.Set(PendingAliasCleanupPreference, true);
                Preferences.Default.Set(SelectedVariantPreference, fileName);
                message = "تم حفظ اختيار الأيقونة، وسيُطبّق التغيير دفعة واحدة عند انتقال التطبيق إلى الخلفية.";
                return true;
            }

            // Android 11/12 fallback: keep the previous deferred-cleanup behavior because
            // these Android versions do not expose the atomic component-state API.
            var newComponent = new Android.Content.ComponentName(
                context.PackageName!, $"{context.PackageName}.{aliasName}");
            packageManager.SetComponentEnabledSetting(
                newComponent,
                Android.Content.PM.ComponentEnabledState.Enabled,
                Android.Content.PM.ComponentEnableOption.DontKillApp);

            Preferences.Default.Set(ActiveAliasPreference, aliasName);
            Preferences.Default.Set(PendingAliasCleanupPreference, true);
            Preferences.Default.Set(SelectedVariantPreference, fileName);

            message = "تم تفعيل الأيقونة الجديدة. ستُزال الأيقونات السابقة عند انتقال التطبيق إلى الخلفية.";
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Launcher Icon] {ex}");
            message = "تم حفظ اختيارك، لكن تعذر تفعيل أيقونة الشاشة الرئيسية: " + ex.Message;
            return false;
        }
#else
        Preferences.Default.Set(SelectedVariantPreference, fileName);
        message = "تم حفظ اختيار الأيقونة. تغيير أيقونة المشغّل مدعوم في إصدار Android.";
        return false;
#endif
    }

    /// <summary>
    /// Switches the enabled launcher component in one PackageManager transaction.
    /// Android documents this API as atomic (API 33+), avoiding an intermediate state
    /// in which both the old and new launcher icons are visible to home-screen launchers.
    /// </summary>
#if ANDROID
    private static void ApplyAliasSelectionAtomically(
        Android.Content.PM.PackageManager packageManager,
        string packageName,
        string activeAlias)
    {
        var allAliases = AliasByFileName.Values
            .Append(DefaultAliasName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var settings = new List<Android.Content.PM.PackageManager.ComponentEnabledSetting>(allAliases.Length);
        foreach (var alias in allAliases)
        {
            var component = new Android.Content.ComponentName(packageName, $"{packageName}.{alias}");
            var state = string.Equals(alias, activeAlias, StringComparison.Ordinal)
                ? Android.Content.PM.ComponentEnabledState.Enabled
                : Android.Content.PM.ComponentEnabledState.Disabled;

            settings.Add(new Android.Content.PM.PackageManager.ComponentEnabledSetting(
                component,
                (int)state,
                (int)Android.Content.PM.ComponentEnableOption.DontKillApp));
        }

        // Every entry uses the same DONT_KILL_APP flag, as required by PackageManager.
        packageManager.SetComponentEnabledSettings(settings);
    }
#endif

    /// <summary>
    /// Finishes the icon switch after the app moves out of the foreground. The stable
    /// LauncherIconDefault native Activity is not a launcher alias and is never disabled.
    /// The operation is safe to retry if Android temporarily rejects a component update.
    /// </summary>
    public static void FinalizePendingAliasChanges()
    {
#if ANDROID
        if (!Preferences.Default.Get(PendingAliasCleanupPreference, false))
            return;

        try
        {
            var context = Android.App.Application.Context;
            var packageManager = context.PackageManager
                ?? throw new InvalidOperationException("تعذر الوصول إلى مدير حزم Android.");

            var activeAlias = Preferences.Default.Get(ActiveAliasPreference, DefaultAliasName);
            var allAliases = AliasByFileName.Values
                .Append(DefaultAliasName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (!allAliases.Contains(activeAlias, StringComparer.Ordinal))
                activeAlias = DefaultAliasName;

            // Android 13+ updates all launcher aliases in one PackageManager transaction.
            // Do not enable the selected alias earlier in TryApply: the launcher must see only
            // the final state, not a temporary period with the old and new icons together.
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                ApplyAliasSelectionAtomically(packageManager, context.PackageName!, activeAlias);
                Preferences.Default.Set(PendingAliasCleanupPreference, false);
                return;
            }

            // Legacy fallback for Android 11/12, where atomic multi-component updates are absent.
            // Ensure the intended icon remains enabled before turning off the others.
            var activeComponent = new Android.Content.ComponentName(
                context.PackageName!, $"{context.PackageName}.{activeAlias}");
            packageManager.SetComponentEnabledSetting(
                activeComponent,
                Android.Content.PM.ComponentEnabledState.Enabled,
                Android.Content.PM.ComponentEnableOption.DontKillApp);

            foreach (var candidateAlias in allAliases)
            {
                if (string.Equals(candidateAlias, activeAlias, StringComparison.Ordinal))
                    continue;

                try
                {
                    var candidate = new Android.Content.ComponentName(
                        context.PackageName!, $"{context.PackageName}.{candidateAlias}");
                    packageManager.SetComponentEnabledSetting(
                        candidate,
                        Android.Content.PM.ComponentEnabledState.Disabled,
                        Android.Content.PM.ComponentEnableOption.DontKillApp);
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Launcher Icon] Could not disable alias {candidateAlias}: {cleanupEx.Message}");
                    // Leave the pending flag set so the next background transition retries cleanup.
                    return;
                }
            }

            Preferences.Default.Set(PendingAliasCleanupPreference, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Launcher Icon] Deferred cleanup failed: {ex}");
        }
#endif
    }

    /// <summary>
    /// Matches the app's in-app palette to the dominant color of the chosen launcher mark.
    /// The user can still choose any other full theme later in the Appearance gallery.
    /// </summary>
    public static void ApplyCompanionInterfaceStyle(string imageFileName)
    {
        var file = Path.GetFileName(imageFileName ?? string.Empty).ToLowerInvariant();
        var styleId = file switch
        {
            "himo_system_icon_original.png" => "p_neon_violet",
            "himo_system_icon_sapphire.png" or "himo_system_icon_azure.png" => "p_electric_blue",
            "himo_system_icon_cyan.png" or "himo_system_icon_turquoise.png" => "p_aurora",
            "himo_system_icon_emerald.png" or "himo_system_icon_green.png" or "himo_system_icon_lime.png" => "p_emerald",
            "himo_system_icon_gold.png" or "himo_system_icon_amber.png" or "himo_system_icon_orange.png" => "p_royal_gold",
            "himo_system_icon_ruby.png" => "p_ruby",
            "himo_system_icon_rose.png" => "p_rose_gold",
            "himo_system_icon_magenta.png" or "himo_system_icon_indigo.png" => "p_neon_violet",
            "himo_glass_icon_purple.png" or "himo_glass_icon_indigo.png" or "himo_glass_icon_magenta.png" => "p_neon_violet",
            "himo_glass_icon_sapphire.png" or "himo_glass_icon_holographic.png" => "p_electric_blue",
            "himo_glass_icon_rose.png" => "p_rose_gold",
            "himo_glass_icon_gold.png" or "himo_glass_icon_amber.png" => "p_royal_gold",
            "himo_glass_icon_turquoise.png" or "himo_glass_icon_icecyan.png" => "p_aurora",
            "himo_glass_icon_ruby.png" => "p_ruby",
            "himo_glass_icon_silver.png" => "p_silver",
            "himo_glass_icon_night.png" => "p_silver",
            "himo_glass_icon_emerald.png" or "himo_glass_icon_lime.png" => "p_emerald",
            _ when file.StartsWith("himo_professional_icon_", StringComparison.Ordinal) => ProfessionalIconStyle(file),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(styleId))
            return;

        HimoAppearanceManager.SetActiveStyle(styleId);
        HimoAppearanceManager.ApplyIconsToCurrentPages();
    }

    private static string? ProfessionalIconStyle(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var numberText = stem.Replace("himo_professional_icon_", string.Empty, StringComparison.Ordinal);
        if (!int.TryParse(numberText, out var number)) return null;

        return number switch
        {
            1 or 3 or 5 or 6 or 11 or 18 or 19 or 22 or 24 or 25 => "p_royal_gold",
            2 or 8 or 10 or 17 or 20 => "p_electric_blue",
            4 or 15 or 21 => "p_neon_violet",
            7 or 13 => "p_ruby",
            9 or 16 => "p_emerald",
            12 => "p_silver",
            14 => "p_rose_gold",
            23 => "p_aurora",
            _ => "p_neon_violet"
        };
    }

    public static string GetSelectedVariant(string fallback = "") =>
        Preferences.Default.Get(SelectedVariantPreference, fallback);
}
