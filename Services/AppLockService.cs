using System.Security.Cryptography;
using System.Text;

namespace Himo.Services;

public sealed class AppLockService
{
    private const string EnabledKey = "himo_app_lock_enabled";
    private const string PinHashKey = "himo_app_lock_pin_hash";
    private const string PinSaltKey = "himo_app_lock_pin_salt";

    public bool IsEnabled => Preferences.Default.Get(EnabledKey, false);

    public async Task<bool> HasPinAsync()
    {
        try
        {
            var hash = await SecureStorage.Default.GetAsync(PinHashKey);
            var salt = await SecureStorage.Default.GetAsync(PinSaltKey);
            return !string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(salt);
        }
        catch
        {
            return false;
        }
    }

    public async Task SetPinAsync(string pin)
    {
        ValidatePin(pin);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPin(pin, salt);
        await SecureStorage.Default.SetAsync(PinSaltKey, Convert.ToBase64String(salt));
        await SecureStorage.Default.SetAsync(PinHashKey, Convert.ToBase64String(hash));
        Preferences.Default.Set(EnabledKey, true);
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return false;
        try
        {
            var saltText = await SecureStorage.Default.GetAsync(PinSaltKey);
            var hashText = await SecureStorage.Default.GetAsync(PinHashKey);
            if (string.IsNullOrWhiteSpace(saltText) || string.IsNullOrWhiteSpace(hashText)) return false;

            var salt = Convert.FromBase64String(saltText);
            var expected = Convert.FromBase64String(hashText);
            var actual = HashPin(pin, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public async Task DisableAsync()
    {
        Preferences.Default.Set(EnabledKey, false);
        try
        {
            SecureStorage.Default.Remove(PinHashKey);
            SecureStorage.Default.Remove(PinSaltKey);
        }
        catch { }
        await Task.CompletedTask;
    }

    private static byte[] HashPin(string pin, byte[] salt)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(pin));
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4 || pin.Length > 6 || pin.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("رمز القفل يجب أن يكون من 4 إلى 6 أرقام.", nameof(pin));
    }
}
