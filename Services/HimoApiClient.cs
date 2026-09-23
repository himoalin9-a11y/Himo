using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Himo.Models;

namespace Himo.Services;

/// <summary>
/// Central HTTP client for the Himo backend. Keep the base URL configurable
/// so the same app can talk to a development PC, emulator, or production API.
/// </summary>
public sealed class HimoApiClient
{
    private const string BaseUrlKey = "himo_api_base_url";
    private const string TokenKey = "himo_api_token";
    private HttpClient _http;

    public HimoApiClient()
    {
        _http = CreateHttpClient(GetBaseUrl());
    }

    private static HttpClient CreateHttpClient(string baseUrl)
    {
        // Use the managed handler consistently for Android HTTP/HTTPS connections.
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(20)
        };

        ApplyToken(client);
        return client;
    }

    public string BaseUrl => _http.BaseAddress?.ToString() ?? string.Empty;
    public bool HasToken => !string.IsNullOrWhiteSpace(GetAccessToken());

    public string? GetAccessToken() => Preferences.Default.Get(TokenKey, string.Empty) is var token && !string.IsNullOrWhiteSpace(token) ? token : null;

    public event EventHandler? SessionExpired;

    private static void ApplyToken(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var token = Preferences.Default.Get(TokenKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ApplyToken() => ApplyToken(_http);

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task SetBaseUrlAsync(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("عنوان الخادم غير صحيح.", nameof(baseUrl));

        var normalized = uri.ToString().TrimEnd('/') + "/";
        Preferences.Default.Set(BaseUrlKey, normalized);

        // HttpClient.BaseAddress cannot be changed after the first request has been sent.
        // Settings can be opened after a failed connection attempt, so replace the
        // client instead of mutating BaseAddress on an already-used instance.
        var oldHttp = _http;
        _http = CreateHttpClient(normalized);
        oldHttp.Dispose();
        await Task.CompletedTask;
    }

    public async Task RequestEmailVerificationAsync(string email, string password, string name, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        ValidatePassword(password);
        name = ValidateName(name);
        using var response = await _http.PostAsJsonAsync("api/auth/request-email-verification", new { email, password, name }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<Account> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        code = code?.Trim() ?? string.Empty;
        if (code.Length != 6 || code.Any(ch => ch < '0' || ch > '9'))
            throw new ArgumentException("رمز التحقق يجب أن يتكون من 6 أرقام.", nameof(code));
        using var response = await _http.PostAsJsonAsync("api/auth/verify-email", new { email, code }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return ApplyAuthResponse(await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken));
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        using var response = await _http.PostAsJsonAsync("api/auth/request-password-reset", new { email }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<Account> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        if (code?.Trim().Length != 6) throw new ArgumentException("رمز التحقق يجب أن يتكون من 6 أرقام.", nameof(code));
        ValidatePassword(newPassword);
        using var response = await _http.PostAsJsonAsync("api/auth/reset-password", new { email, code = code.Trim(), newPassword }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return ApplyAuthResponse(await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken));
    }

    public async Task<Account> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        ValidatePassword(password);
        using var response = await _http.PostAsJsonAsync("api/auth/login-email", new { email, password }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return ApplyAuthResponse(await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken));
    }

    private Account ApplyAuthResponse(AuthResponse? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Token) || string.IsNullOrWhiteSpace(result.Email))
            throw new InvalidOperationException("استجابة تسجيل الدخول غير صالحة.");
        Preferences.Default.Set(TokenKey, result.Token);
        ApplyToken();
        return new Account { Email = result.Email, Name = result.Name };
    }

    private static string NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length is < 5 or > 254 || !email.Contains('@') || email.Contains(' '))
            throw new ArgumentException("البريد الإلكتروني غير صحيح.", nameof(value));
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1 || !email[(at + 1)..].Contains('.'))
            throw new ArgumentException("البريد الإلكتروني غير صحيح.", nameof(value));
        return email;
    }

    private static void ValidatePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8 || value.Length > 128)
            throw new ArgumentException("كلمة المرور يجب أن تكون بين 8 و128 حرفًا.", nameof(value));
    }

    private static string ValidateName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
            throw new ArgumentException("الاسم مطلوب وبحد أقصى 60 حرفًا.", nameof(value));
        return name;
    }

    public async Task<UserProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/me", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("استجابة الملف الشخصي غير صالحة.");
    }

    public async Task<UserProfileResponse> UpdateMyProfileAsync(string name, string status, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PutAsJsonAsync("api/me", new { name, status }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("استجابة الملف الشخصي غير صالحة.");
    }

    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync("api/me", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        ClearToken();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (HasToken)
            {
                using var response = await _http.PostAsync("api/auth/logout", content: null, cancellationToken);
            }
        }
        finally
        {
            ClearToken();
        }
    }

    public void ClearToken()
    {
        Preferences.Default.Remove(TokenKey);
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task RegisterPushTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        using var response = await _http.PostAsJsonAsync("api/push-token", new { token }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemovePushTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/push-token")
        {
            Content = JsonContent.Create(new { token })
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationDto>> GetConversationsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/conversations", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<ConversationDto>>(cancellationToken: cancellationToken) ?? new List<ConversationDto>();
    }

    public async Task MarkConversationReadAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync($"api/conversations/{conversationId}/read", content: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<ConversationDto> CreateConversationAsync(string name, CancellationToken cancellationToken = default)
        => CreateConversationAsync(name, null, cancellationToken);

    public async Task<IReadOnlyList<UserSearchDto>> SearchUsersAsync(string email, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        var encoded = Uri.EscapeDataString(email);
        using var response = await _http.GetAsync($"api/users/search?email={encoded}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<UserSearchDto>>(cancellationToken: cancellationToken) ?? new List<UserSearchDto>();
    }

    public async Task<ConversationDto> CreateConversationAsync(string name, Guid? userId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/conversations", new { name, userId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConversationDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("استجابة الخادم غير صالحة.");
    }

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid conversationId, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        var path = $"api/conversations/{conversationId}/messages";
        if (since.HasValue)
            path += $"?since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        using var response = await _http.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<MessageDto>>(cancellationToken: cancellationToken) ?? new List<MessageDto>();
    }

    public async Task<MessageDto> SendMessageAsync(Guid conversationId, string text, string? clientMessageId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync($"api/conversations/{conversationId}/messages", new { text, clientMessageId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("استجابة الخادم غير صالحة.");
    }

    public async Task<MessageDto> UploadAttachmentAsync(Guid conversationId, FileResult file, CancellationToken cancellationToken = default)
    {
        await using var stream = await file.OpenReadAsync();
        return await UploadAttachmentAsync(conversationId, stream, file.FileName, file.ContentType, cancellationToken);
    }

    public async Task<MessageDto> UploadAttachmentAsync(Guid conversationId, Stream stream, string fileName, string? contentType, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        content.Add(fileContent, "file", fileName);
        using var response = await _http.PostAsync($"api/conversations/{conversationId}/attachments", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("استجابة المرفق غير صالحة.");
    }

    public async Task<string> DownloadAttachmentAsync(Guid messageId, string fileName, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"api/messages/{messageId}/attachment", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var safeName = Path.GetFileName(fileName);
        var path = Path.Combine(FileSystem.Current.CacheDirectory, $"himo_{messageId:N}_{safeName}");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken);
        return path;
    }

    private static string NormalizeAndValidatePhone(string? value)
    {
        var original = value?.Trim() ?? string.Empty;
        var normalized = PhoneNumberUtils.Normalize(original);
        if (!PhoneNumberUtils.IsValid(original))
            throw new ArgumentException("رقم الهاتف غير صحيح.", nameof(value));
        return normalized;
    }

    private static string NormalizeDigits(string? value)
    {
        var input = value ?? string.Empty;
        var chars = new char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            chars[i] = input[i] switch
            {
                >= '٠' and <= '٩' => (char)('0' + (input[i] - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (input[i] - '۰')),
                _ => input[i]
            };
        }
        return new string(chars);
    }

    private static string GetBaseUrl()
    {
        // Production/default endpoint: the Himo API is hosted externally over HTTPS.
        // Keep the URL configurable through Settings so development/LAN servers can
        // still be used when explicitly selected by the user.
        const string productionUrl = "https://himo-3buh.onrender.com/";
        var saved = Preferences.Default.Get(BaseUrlKey, string.Empty).Trim();

        // Migrate installations that still contain the previous PC/LAN endpoint.
        if (string.IsNullOrWhiteSpace(saved) ||
            saved.Contains("192.168.8.85", StringComparison.OrdinalIgnoreCase) ||
            saved.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            saved.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            Preferences.Default.Set(BaseUrlKey, productionUrl);
            return productionUrl;
        }

        return saved.EndsWith('/') ? saved : saved + "/";
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var message = await TryReadMessageAsync(response, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && HasToken)
        {
            // A 401 from an authenticated request means the local bearer token
            // is no longer valid. Do not treat unauthenticated 401 responses
            // (for example, an incorrect OTP during login) as session expiry.
            ClearToken();
            SessionExpired?.Invoke(this, EventArgs.Empty);
            throw new HttpRequestException("انتهت جلسة تسجيل الدخول. سجّل الدخول مرة أخرى.");
        }

        throw new HttpRequestException(message ?? $"الخادم أعاد الحالة {(int)response.StatusCode}.");
    }

    private static async Task<string?> TryReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: cancellationToken);
            return body?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record ConversationDto(Guid Id, string Name, string LastMessage, DateTimeOffset UpdatedAt, int UnreadCount);
    public sealed record MessageDto(Guid Id, Guid ConversationId, Guid SenderUserId, string SenderPhoneNumber, string Text, DateTimeOffset SentAt, string? AttachmentFileName = null, string? AttachmentContentType = null, long? AttachmentSize = null);
    private sealed record AuthResponse(string Token, string Email, string Name);
    public sealed record UserProfileResponse(string Email, string Name, string Status);
    private sealed record RequestCodeResponse(string Message, string? DevelopmentCode);
    private sealed record ApiError(string? Message);
}
