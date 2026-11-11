using Himo.Services;

namespace Himo.Services;

/// <summary>
/// Backward-compatible facade for older callers. AppLocalization is the single
/// source of truth for supported languages, persistence, culture and UI refresh.
/// </summary>
[Obsolete("Use AppLocalization directly. This facade delegates to the unified localization service.")]
public static class HimoLanguageManager
{
    public static string CurrentLanguageCode => AppLocalization.CurrentLanguageCode;

    public static void ApplySavedLanguage() => AppLocalization.InitializeFromPreferences();

    public static void SetLanguage(string languageCode) => AppLocalization.SetLanguage(languageCode);
}
