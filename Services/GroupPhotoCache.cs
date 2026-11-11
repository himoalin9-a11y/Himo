using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>Versioned local cache for group avatars.</summary>
public static class GroupPhotoCache
{
    public static string? Find(Guid groupId, long version)
    {
        try
        {
            var directory = FileSystem.Current.CacheDirectory;
            if (!Directory.Exists(directory)) return null;
            return Directory.EnumerateFiles(directory, $"himo_group_photo_{groupId:N}_v{Math.Max(0, version)}.*")
                .FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    public static async Task<string?> GetPathAsync(HimoApiClient api, Guid groupId, bool hasPhoto, long version, CancellationToken cancellationToken = default)
    {
        var directory = FileSystem.Current.CacheDirectory;
        Directory.CreateDirectory(directory);
        if (!hasPhoto) { RemoveOld(directory, groupId, null); return null; }
        var cached = Find(groupId, version);
        if (cached is not null) return cached;
        var download = await api.GetGroupPhotoAsync(groupId, cancellationToken);
        if (download is null) return null;
        var extension = download.ContentType.Trim().ToLowerInvariant() switch
        {
            "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".jpg"
        };
        var destination = Path.Combine(directory, $"himo_group_photo_{groupId:N}_v{Math.Max(0, version)}{extension}");
        var temp = Path.Combine(directory, "himo_group_photo_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, download.Bytes, cancellationToken);
            File.Move(temp, destination, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        RemoveOld(directory, groupId, destination);
        return File.Exists(destination) ? destination : null;
    }

    private static void RemoveOld(string directory, Guid groupId, string? keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, $"himo_group_photo_{groupId:N}_v*.*"))
                if (keep is null || !string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(file); } catch { }
        }
        catch { }
    }
}
