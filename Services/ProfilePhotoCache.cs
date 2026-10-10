using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>
/// Local cache for authenticated users' profile photos. The server-side version
/// number changes whenever a photo is uploaded or removed, so cached filenames
/// are naturally invalidated without repeatedly downloading unchanged photos.
/// </summary>
public static class ProfilePhotoCache
{
    public static string? Find(Guid userId, long version)
    {
        try
        {
            var directory = FileSystem.Current.CacheDirectory;
            if (!Directory.Exists(directory)) return null;
            var pattern = $"himo_user_profile_{userId:N}_v{Math.Max(0, version)}.*";
            return Directory.EnumerateFiles(directory, pattern)
                .FirstOrDefault(path => File.Exists(path));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> GetPathAsync(
        HimoApiClient api,
        Guid userId,
        bool hasPhoto,
        long version,
        CancellationToken cancellationToken = default)
    {
        var directory = FileSystem.Current.CacheDirectory;
        Directory.CreateDirectory(directory);

        if (!hasPhoto)
        {
            RemoveCachedPhotos(directory, userId);
            return null;
        }

        var cached = Find(userId, version);
        if (cached is not null) return cached;

        var downloaded = await api.GetUserProfilePhotoAsync(userId, cancellationToken);
        if (downloaded is null) return null;

        var extension = ExtensionFor(downloaded.ContentType);
        var destination = Path.Combine(
            directory,
            $"himo_user_profile_{userId:N}_v{Math.Max(0, version)}{extension}");

        if (File.Exists(destination)) return destination;

        var temporary = Path.Combine(directory, "himo_photo_download_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, downloaded.Bytes, cancellationToken);
            try
            {
                File.Move(temporary, destination, overwrite: true);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another page downloaded this same user's photo at the same time.
            }
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }

        RemoveOldVersions(directory, userId, destination);
        return File.Exists(destination) ? destination : null;
    }

    private static string ExtensionFor(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "image/gif" => ".gif",
            _ => ".img"
        };

    private static void RemoveOldVersions(string directory, Guid userId, string keepPath)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, $"himo_user_profile_{userId:N}_v*.*"))
            {
                if (string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(path); } catch { }
            }
        }
        catch { }
    }

    private static void RemoveCachedPhotos(string directory, Guid userId)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, $"himo_user_profile_{userId:N}_v*.*"))
            {
                try { File.Delete(path); } catch { }
            }
        }
        catch { }
    }
}
