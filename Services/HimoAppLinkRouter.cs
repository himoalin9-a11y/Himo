using System.Text;
using Himo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Himo.Services;

public enum HimoAppLinkTargetKind
{
    OwnProfile,
    PublicProfile,
    Conversation
}

public sealed record HimoAppLinkResolution(
    HimoAppLinkTargetKind Kind,
    Guid TargetId,
    string? DisplayName = null);

/// <summary>
/// Validates Himo custom-scheme links and HTTPS share links, then resolves them
/// to a normalized himo:// URI before navigating inside the application.
/// </summary>
public static class HimoAppLinkRouter
{
    private static readonly HashSet<string> KnownPublicHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "himo-api.onrender.com",
        "himo-3buh.onrender.com"
    };

    public static async Task<HimoAppLinkResolution> ResolveAsync(string rawLink, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawLink);
        ArgumentNullException.ThrowIfNull(services);

        var api = services.GetRequiredService<HimoApiClient>();
        if (!TryNormalizeLink(rawLink, out var uri, api.BaseUrl))
            throw new InvalidOperationException("رابط Himo غير صالح أو غير موثوق.");

        if (uri.Host.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var userId) || userId == Guid.Empty)
                throw new InvalidOperationException("رابط حساب Himo لا يحتوي على معرّف صالح.");

            // Compare against the authenticated account so sharing one's own link
            // returns to the local profile instead of attempting to message oneself.
            var ownProfile = await api.GetMyProfileAsync();
            if (ownProfile.UserId == userId)
                return new HimoAppLinkResolution(HimoAppLinkTargetKind.OwnProfile, userId, ownProfile.Name);

            return new HimoAppLinkResolution(
                HimoAppLinkTargetKind.PublicProfile,
                userId,
                ReadDisplayName(uri) ?? "مستخدم Himo");
        }

        if (uri.Host.Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var groupId) || groupId == Guid.Empty)
                throw new InvalidOperationException("رابط المجموعة لا يحتوي على معرّف صالح.");

            var inviteCode = ReadQueryValue(uri, "invite");
            if (string.IsNullOrWhiteSpace(inviteCode))
                throw new InvalidOperationException("رابط المجموعة لا يحتوي على رمز دعوة.");

            var home = services.GetRequiredService<HomeViewModel>();
            var conversation = await home.JoinGroupByInviteAsync(inviteCode, groupId);
            if (!Guid.TryParse(conversation.RemoteId, out var joinedGroupId) || joinedGroupId != groupId)
                throw new InvalidOperationException("رمز الدعوة لا يطابق المجموعة الموجودة في الرابط.");

            return new HimoAppLinkResolution(HimoAppLinkTargetKind.Conversation, groupId, conversation.Name);
        }

        throw new InvalidOperationException("نوع رابط Himo غير مدعوم.");
    }

    /// <summary>
    /// Converts trusted HTTPS landing links /join/user/{id} and /join/group/{id}
    /// into the existing himo://user or himo://group routes. Old custom-scheme
    /// links remain supported for backward compatibility.
    /// </summary>
    public static bool TryNormalizeLink(string? rawLink, out Uri normalizedUri, string? trustedPublicBaseUrl = null)
    {
        normalizedUri = null!;
        if (!Uri.TryCreate(rawLink?.Trim(), UriKind.Absolute, out var source) ||
            !string.IsNullOrEmpty(source.UserInfo))
            return false;

        if (source.Scheme.Equals("himo", StringComparison.OrdinalIgnoreCase))
        {
            if (source.Host.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                source.Host.Equals("group", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUri = source;
                return true;
            }
            return false;
        }

        if (!source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsTrustedPublicHost(source, trustedPublicBaseUrl))
            return false;

        var segments = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 || !segments[0].Equals("join", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Guid.TryParse(Uri.UnescapeDataString(segments[2]), out var targetId) || targetId == Guid.Empty)
            return false;

        if (segments[1].Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            var name64 = ReadQueryValue(source, "name64");
            var suffix = string.IsNullOrWhiteSpace(name64) ? string.Empty : $"?name64={Uri.EscapeDataString(name64)}";
            normalizedUri = new Uri($"himo://user/{targetId:D}{suffix}", UriKind.Absolute);
            return true;
        }

        if (segments[1].Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            var inviteCode = ReadQueryValue(source, "invite");
            if (string.IsNullOrWhiteSpace(inviteCode) || inviteCode.Length is < 16 or > 80)
                return false;
            normalizedUri = new Uri(
                $"himo://group/{targetId:D}?invite={Uri.EscapeDataString(inviteCode)}",
                UriKind.Absolute);
            return true;
        }

        return false;
    }

    public static bool IsSupportedLink(string? rawLink, string? trustedPublicBaseUrl = null)
        => TryNormalizeLink(rawLink, out _, trustedPublicBaseUrl);

    // Compatibility wrapper for callers that still expect a conversation-only link.
    public static async Task<string> ResolveConversationIdAsync(string rawLink, IServiceProvider services)
    {
        var result = await ResolveAsync(rawLink, services);
        if (result.Kind != HimoAppLinkTargetKind.Conversation)
            throw new InvalidOperationException("هذا الرابط يفتح ملفًا شخصيًا وليس محادثة.");
        return result.TargetId.ToString("D");
    }

    private static bool IsTrustedPublicHost(Uri uri, string? trustedPublicBaseUrl)
    {
        if (uri.IsDefaultPort == false && uri.Port != 443)
            return false;

        if (KnownPublicHosts.Contains(uri.IdnHost))
            return true;

        if (!Uri.TryCreate(trustedPublicBaseUrl, UriKind.Absolute, out var trusted) ||
            !trusted.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.IdnHost.Equals(trusted.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               (trusted.IsDefaultPort || trusted.Port == uri.Port);
    }

    public static string? ReadDisplayName(Uri uri)
    {
        var encoded = ReadQueryValue(uri, "name64");
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            try
            {
                var token = encoded.Replace('-', '+').Replace('_', '/');
                token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Trim();
                if (decoded.Length is > 0 and <= 60) return decoded;
            }
            catch (FormatException) { }
        }

        var name = ReadQueryValue(uri, "name");
        return !string.IsNullOrWhiteSpace(name) && name.Length <= 60 ? name : null;
    }

    public static string? ReadQueryValue(Uri uri, string key)
    {
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2) continue;
            try
            {
                if (!Uri.UnescapeDataString(pair[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return Uri.UnescapeDataString(pair[1].Replace('+', ' ')).Trim();
            }
            catch (UriFormatException)
            {
                return null;
            }
        }
        return null;
    }
}
