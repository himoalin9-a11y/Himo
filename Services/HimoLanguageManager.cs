using System.Globalization;
using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>Persists Himo's interface language independently from the device language.</summary>
public static class HimoLanguageManager
{
    private const string LanguagePreferenceKey = "Himo.AppLanguageCode";

    public static string CurrentLanguageCode
    {
        get
        {
            var saved = Preferences.Default.Get(LanguagePreferenceKey, string.Empty);
            if (saved.Equals("en", StringComparison.OrdinalIgnoreCase)) return "en";
            if (saved.Equals("ar", StringComparison.OrdinalIgnoreCase)) return "ar";

            // Preserve Himo's existing Arabic default until the user chooses otherwise.
            return "ar";
        }
    }

    public static void ApplySavedLanguage() => ApplyCulture(CurrentLanguageCode);

    public static void SetLanguage(string languageCode)
    {
        var normalized = languageCode?.Trim().ToLowerInvariant();
        if (normalized is not ("ar" or "en"))
            throw new ArgumentOutOfRangeException(nameof(languageCode), "Supported Himo languages are ar and en.");

        Preferences.Default.Set(LanguagePreferenceKey, normalized);
        ApplyCulture(normalized);
    }

    private static void ApplyCulture(string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(languageCode == "en" ? "en-US" : "ar-SA");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
