using System.Text.Json;
using Himo.Models;

namespace Himo.Services;

public sealed class ProfileService
{
    private const string StoreFileName = "himo_profile.json";
    private readonly string _storePath;
    private readonly object _sync = new();
    private UserProfile _profile = new();
    private bool _loaded;

    public UserProfile Profile
    {
        get { lock (_sync) { EnsureLoaded(); return _profile; } }
    }

    public ProfileService()
    {
        _storePath = Path.Combine(FileSystem.Current.AppDataDirectory, StoreFileName);
        Load();
    }

    public void Update(string name, string status)
    {
        var cleanName = name.Trim();
        var cleanStatus = status.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            throw new ArgumentException("الاسم مطلوب.", nameof(name));
        if (cleanName.Length > 60)
            throw new ArgumentException("الاسم طويل جدًا.", nameof(name));
        if (cleanStatus.Length > 100)
            throw new ArgumentException("الحالة طويلة جدًا.", nameof(status));

        lock (_sync)
        {
            EnsureLoaded();
            _profile.Name = cleanName;
            _profile.Status = string.IsNullOrWhiteSpace(cleanStatus) ? "متاح على Himo" : cleanStatus;
            Save();
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
                if (File.Exists(_storePath))
                {
                    var json = File.ReadAllText(_storePath);
                    var value = JsonSerializer.Deserialize<UserProfile>(json);
                    if (value != null && !string.IsNullOrWhiteSpace(value.Name))
                    {
                        _profile = value;
                        return;
                    }
                }
            }
            catch { }
            Save();
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
            File.WriteAllText(_storePath, JsonSerializer.Serialize(_profile));
        }
        catch { }
    }
}
