using System.Security.Cryptography;
using System.Text;
using Himo.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>Device-local preferences for conversation presentation and security.</summary>
public static class ConversationFeatureStore
{
    public static string ConversationKey(Conversation conversation) =>
        !string.IsNullOrWhiteSpace(conversation.RemoteId)
            ? conversation.RemoteId.Trim().ToLowerInvariant()
            : $"local-{conversation.Id}";

    private static string Key(string conversationKey, string suffix) => $"himo_conversation_{conversationKey}_{suffix}";
    public static bool IsFavorite(string key) => Preferences.Default.Get(Key(key, "favorite"), false);
    public static void SetFavorite(string key, bool value) => Preferences.Default.Set(Key(key, "favorite"), value);
    public static bool IsTranslationEnabled(string key) => Preferences.Default.Get(Key(key, "translation"), false);
    public static void SetTranslationEnabled(string key, bool value) => Preferences.Default.Set(Key(key, "translation"), value);
    public static bool IsLocked(string key) => Preferences.Default.Get(Key(key, "locked"), false);
    public static void SetLocked(string key, bool value) => Preferences.Default.Set(Key(key, "locked"), value);
    public static int GetLocalDisappearingSeconds(string key) => Preferences.Default.Get(Key(key, "disappearing_seconds"), 0);
    public static void SetLocalDisappearingSeconds(string key, int value) => Preferences.Default.Set(Key(key, "disappearing_seconds"), value);

    private static string PinStorageKey(string key) => $"himo_conversation_pin_{key}";
    private static string PinSaltKey(string key) => Key(key, "pin_salt");

    public static async Task<bool> HasPinAsync(string key)
    {
        try { return !string.IsNullOrWhiteSpace(await SecureStorage.Default.GetAsync(PinStorageKey(key))); }
        catch { return false; }
    }

    public static async Task<bool> ConfigurePinAsync(Page page, string key)
    {
        var first = await page.DisplayPromptAsync("قفل الدردشة", "أنشئ رمزًا من 4 إلى 8 أرقام", "متابعة", "إلغاء", "الرمز", maxLength: 8, keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(first)) return false;
        var pin = first.Trim();
        if (pin.Length is < 4 or > 8 || pin.Any(c => !char.IsDigit(c)))
        {
            await page.DisplayAlertAsync("رمز غير صالح", "يجب أن يتكون الرمز من 4 إلى 8 أرقام.", "حسنًا");
            return false;
        }
        var second = await page.DisplayPromptAsync("تأكيد الرمز", "أعد إدخال الرمز للتأكيد", "حفظ", "إلغاء", "الرمز", maxLength: 8, keyboard: Keyboard.Numeric);
        if (!string.Equals(pin, second?.Trim(), StringComparison.Ordinal))
        {
            await page.DisplayAlertAsync("الرمزان غير متطابقين", "لم يتم حفظ الرمز. حاول مرة أخرى.", "حسنًا");
            return false;
        }

        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var hash = HashPin(salt, pin);
        try
        {
            await SecureStorage.Default.SetAsync(PinStorageKey(key), hash);
            Preferences.Default.Set(PinSaltKey(key), salt);
            SetLocked(key, true);
            return true;
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("تعذر حفظ الرمز", "تعذر تخزين الرمز الآمن على هذا الجهاز. " + ex.Message, "حسنًا");
            return false;
        }
    }

    public static async Task<bool> VerifyPinAsync(string key, string pin)
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(PinStorageKey(key));
            var salt = Preferences.Default.Get(PinSaltKey(key), string.Empty);
            if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(salt)) return false;
            var candidate = HashPin(salt, pin.Trim());
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored), Convert.FromHexString(candidate));
        }
        catch { return false; }
    }

    private static string HashPin(string salt, string pin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"Himo-chat-lock|{salt}|{pin}")));
}
