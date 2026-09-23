using System.Text.Json;
using Himo.Models;

namespace Himo.Services;

public sealed class AccountService
{
    private const string StoreFileName = "himo_account.json";
    private readonly string _storePath;
    private readonly object _sync = new();
    private Account? _account;
    private bool _loaded;

    public bool IsSignedIn
    {
        get { lock (_sync) { EnsureLoaded(); return _account != null; } }
    }

    public Account? CurrentAccount
    {
        get { lock (_sync) { EnsureLoaded(); return _account; } }
    }

    public AccountService()
    {
        _storePath = Path.Combine(FileSystem.Current.AppDataDirectory, StoreFileName);
        Load();
    }

    public void SignIn(string email, string name)
    {
        var cleanEmail = email.Trim().ToLowerInvariant();
        var cleanName = name.Trim();
        if (string.IsNullOrWhiteSpace(cleanEmail)) throw new ArgumentException("البريد الإلكتروني مطلوب.", nameof(email));
        if (!cleanEmail.Contains('@') || cleanEmail.Length > 254) throw new ArgumentException("البريد الإلكتروني غير صحيح.", nameof(email));
        if (string.IsNullOrWhiteSpace(cleanName)) throw new ArgumentException("الاسم مطلوب.", nameof(name));
        if (cleanName.Length > 60) throw new ArgumentException("الاسم طويل جدًا.", nameof(name));

        lock (_sync)
        {
            EnsureLoaded();
            _account = new Account { Email = cleanEmail, Name = cleanName };
            Save();
        }
    }

    public void SignOut()
    {
        lock (_sync)
        {
            _account = null;
            try { if (File.Exists(_storePath)) File.Delete(_storePath); } catch { }
        }
    }

    private void Load()
    {
        lock (_sync)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(_storePath)) return;
                var json = File.ReadAllText(_storePath);
                var value = JsonSerializer.Deserialize<Account>(json);
                if (value != null && !string.IsNullOrWhiteSpace(value.Email) && !string.IsNullOrWhiteSpace(value.Name))
                    _account = value;
            }
            catch { _account = null; }
        }
    }

    private void EnsureLoaded()
    {
        if (!_loaded) Load();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(FileSystem.Current.AppDataDirectory);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(_account));
        }
        catch { }
    }
}
