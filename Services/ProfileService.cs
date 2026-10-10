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


    public string SavePhoto(byte[] imageBytes, string contentType)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0 || imageBytes.Length > 4 * 1024 * 1024)
            throw new ArgumentException("حجم الصورة يجب ألا يتجاوز 4 ميغابايت.", nameof(imageBytes));

        var extension = contentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "image/gif" => ".gif",
            _ => throw new ArgumentException("صيغة الصورة غير مدعومة.", nameof(contentType))
        };

        lock (_sync)
        {
            EnsureLoaded();
            var directory = FileSystem.Current.AppDataDirectory;
            Directory.CreateDirectory(directory);
            // Use a fresh path for every update. MAUI's image loader can cache a
            // previous image by path, so overwriting one fixed filename may keep
            // showing the old picture even though upload/save succeeded.
            var path = Path.Combine(directory, $"himo_profile_photo_{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}{extension}");
            var temporaryPath = path + ".tmp";

            // Write first so a failed write never deletes the currently saved photo.
            File.WriteAllBytes(temporaryPath, imageBytes);
            File.Move(temporaryPath, path, true);

            foreach (var oldPath in Directory.EnumerateFiles(directory, "himo_profile_photo*.*"))
            {
                if (string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(oldPath, temporaryPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(oldPath); } catch { }
            }

            _profile.PhotoPath = path;
            Save();
            return path;
        }
    }

    public void ClearPhoto()
    {
        lock (_sync)
        {
            EnsureLoaded();
            var previousPath = _profile.PhotoPath;
            _profile.PhotoPath = null;
            Save();

            if (!string.IsNullOrWhiteSpace(previousPath))
            {
                try { if (File.Exists(previousPath)) File.Delete(previousPath); } catch { }
            }

            try
            {
                var directory = FileSystem.Current.AppDataDirectory;
                foreach (var oldPath in Directory.EnumerateFiles(directory, "himo_profile_photo*.*"))
                {
                    try { File.Delete(oldPath); } catch { }
                }
            }
            catch { }
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
