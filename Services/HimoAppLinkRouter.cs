using System.Diagnostics.CodeAnalysis;
using System.Text;
using Himo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Himo.Services;

public enum HimoAppLinkTargetKind
{
    OwnProfile,
    Conversation,
    GroupInvite
}

public sealed record HimoAppLinkResolution(
    HimoAppLinkTargetKind Kind,
    Guid TargetId,
    string? DisplayName = null,
    string? InviteCode = null);

/// <summary>Normalizes and resolves trusted Himo links to an in-app destination.</summary>
public static class HimoAppLinkRouter
{
    private static readonly HashSet<string> KnownPublicHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "himo-api.onrender.com",};

    public static async Task<HimoAppLinkResolution> ResolveAsync(string rawLink, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawLink);
        ArgumentNullException.ThrowIfNull(services);

        var api = services.GetRequiredService<HimoApiClient>();
        if (!TryNormalizeLink(rawLink, out var uri, api.BaseUrl) || uri is null)
            throw new InvalidOperationException("رابط Himo غير صالح أو غير موثوق.");

        if (uri.Host.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var userId) || userId == Guid.Empty)
                throw new InvalidOperationException("رابط حساب Himo لا يحتوي على معرّف صالح.");

            // Use the account cache first. This avoids an unnecessary /api/me
            // round-trip for the common case while retaining a safe fallback after
            // token restoration on a cold start.
            var account = services.GetRequiredService<AccountService>();
            var currentUserId = account.CurrentAccount?.UserId ?? Guid.Empty;
            string? currentUserName = account.CurrentAccount?.Name;
            if (currentUserId == Guid.Empty)
            {
                var ownProfile = await api.GetMyProfileAsync();
                currentUserId = ownProfile.UserId;
                currentUserName = ownProfile.Name;
                if (currentUserId != Guid.Empty)
                    account.SetUserId(currentUserId);
            }

            if (currentUserId == userId)
                return new HimoAppLinkResolution(HimoAppLinkTargetKind.OwnProfile, userId, currentUserName);

            var home = services.GetRequiredService<HomeViewModel>();

            // Fast path: if this direct chat is already in the local conversation
            // cache, navigate immediately. ChatPage will refresh its message state.
            var cachedConversation = home.Conversations.FirstOrDefault(item =>
                !item.IsGroup &&
                item.OtherParticipantUserId == userId &&
                Guid.TryParse(item.RemoteId, out var cachedId) &&
                cachedId != Guid.Empty);
            if (cachedConversation is not null &&
                Guid.TryParse(cachedConversation.RemoteId, out var cachedConversationId) &&
                cachedConversationId != Guid.Empty)
            {
                return new HimoAppLinkResolution(
                    HimoAppLinkTargetKind.Conversation,
                    cachedConversationId,
                    cachedConversation.Name);
            }

            // A single authenticated POST creates or reuses the direct conversation.
            // The API returns the peer's canonical name and photo metadata, so a
            // separate public-profile GET is redundant and only adds latency.
            var displayName = ReadDisplayName(uri) ?? "محادثة";
            var directConversation = await home.CreateConversationWithUserAsync(displayName, userId);
            if (!Guid.TryParse(directConversation.RemoteId, out var directConversationId) || directConversationId == Guid.Empty)
                throw new InvalidOperationException("تعذر فتح محادثة هذا المستخدم.");

            return new HimoAppLinkResolution(HimoAppLinkTargetKind.Conversation, directConversationId, directConversation.Name);
        }

        if (uri.Host.Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var groupId) || groupId == Guid.Empty)
                throw new InvalidOperationException("رابط المجموعة لا يحتوي على معرّف صالح.");

            var inviteCode = ReadQueryValue(uri, "invite");
            if (string.IsNullOrWhiteSpace(inviteCode))
                throw new InvalidOperationException("رابط المجموعة لا يحتوي على رمز دعوة.");

            // Resolve only the invite data here. MainActivity shows the native
            // confirmation dialog and calls the join API only after explicit approval.
            return new HimoAppLinkResolution(
                HimoAppLinkTargetKind.GroupInvite,
                groupId,
                InviteCode: inviteCode);
        }

        throw new InvalidOperationException("نوع رابط Himo غير مدعوم.");
    }

    /// <summary>
    /// Converts trusted HTTPS landing links (/join/user/{id} and /join/group/{id})
    /// into the existing himo:// routes. Previous custom-scheme links remain supported.
    /// </summary>
    public static bool TryNormalizeLink(
        string? rawLink,
        [NotNullWhen(true)] out Uri? normalizedUri,
        string? trustedPublicBaseUrl = null)
    {
        normalizedUri = null;
        if (!Uri.TryCreate(rawLink?.Trim(), UriKind.Absolute, out var source) ||
            source is null ||
            !string.IsNullOrEmpty(source.UserInfo))
            return false;

        if (source.Scheme.Equals("himo", StringComparison.OrdinalIgnoreCase))
        {
            if (!source.Host.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                !source.Host.Equals("group", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Guid.TryParse(Uri.UnescapeDataString(source.AbsolutePath).Trim('/'), out var customTargetId) ||
                customTargetId == Guid.Empty)
                return false;

            if (source.Host.Equals("group", StringComparison.OrdinalIgnoreCase))
            {
                var customInvite = ReadQueryValue(source, "invite");
                if (string.IsNullOrWhiteSpace(customInvite) || customInvite.Length is < 16 or > 80)
                    return false;
            }

            normalizedUri = source;
            return true;
        }

        if (!source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            (!source.IsDefaultPort && source.Port != 443) ||
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
            var name = ReadQueryValue(source, "name");
            var query = !string.IsNullOrWhiteSpace(name64)
                ? $"?name64={Uri.EscapeDataString(name64)}"
                : !string.IsNullOrWhiteSpace(name)
                    ? $"?name={Uri.EscapeDataString(name)}"
                    : string.Empty;

            normalizedUri = new Uri($"himo://user/{targetId:D}{query}", UriKind.Absolute);
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

    // Compatibility wrapper for existing callers that expect a conversation-only link.
    public static async Task<string> ResolveConversationIdAsync(string rawLink, IServiceProvider services)
    {
        var result = await ResolveAsync(rawLink, services);
        if (result.Kind != HimoAppLinkTargetKind.Conversation)
            throw new InvalidOperationException("هذا الرابط لا يفتح محادثة مباشرة.");
        return result.TargetId.ToString("D");
    }

    private static bool IsTrustedPublicHost(Uri uri, string? trustedPublicBaseUrl)
    {
        if (KnownPublicHosts.Contains(uri.IdnHost))
            return true;

        if (!Uri.TryCreate(trustedPublicBaseUrl, UriKind.Absolute, out var trusted) || trusted is null ||
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
