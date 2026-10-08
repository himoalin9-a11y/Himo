using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Himo.Models;

#if ANDROID
using Xamarin.Android.Net;
#endif

namespace Himo.Services;

/// <summary>
/// Central HTTP client for the Himo backend.
/// Uses Android's native HTTP stack on Android and the standard
/// managed HTTP handler on other platforms.
/// </summary>
public sealed class HimoApiClient
{
    private const string BaseUrlKey = "himo_api_base_url";
    private const string TokenKey = "himo_api_token";

    private HttpClient _http;
    private string? _accessToken;
    private readonly Task _tokenInitialization;

    public Task TokenInitialization => _tokenInitialization;

    public HimoApiClient()
    {
        _http = CreateHttpClient(GetBaseUrl());
        _tokenInitialization = InitializeTokenAsync();
    }

    private static HttpClient CreateHttpClient(string baseUrl)
    {
#if ANDROID
        // AndroidMessageHandler uses Android's native HTTP stack.
        // This is preferable here because the application is running on
        // Android and Chrome on the same device can already resolve/reach
        // the Render HTTPS endpoint successfully.
        var handler = new AndroidMessageHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,

            ConnectTimeout = TimeSpan.FromSeconds(20),
            ReadTimeout = TimeSpan.FromSeconds(60),

            // Do not disable the Android/device proxy configuration.
            // The previous SocketsHttpHandler explicitly used UseProxy=false.
            UseProxy = true,

            AllowAutoRedirect = true
        };
#else
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,

            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

            // Respect the normal platform proxy configuration.
            UseProxy = true,

            AllowAutoRedirect = true
        };
#endif

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };

        ApplyToken(client, null);

        return client;
    }

    public string BaseUrl =>
        _http.BaseAddress?.ToString() ?? string.Empty;

    public bool HasToken =>
        !string.IsNullOrWhiteSpace(GetAccessToken());

    public string? GetAccessToken() =>
        _accessToken;

    public event EventHandler? SessionExpired;

    private static void ApplyToken(HttpClient client, string? token)
    {
        client.DefaultRequestHeaders.Authorization = null;

        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private void ApplyToken() =>
        ApplyToken(_http, _accessToken);

    public async Task<bool> HealthAsync(
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.GetAsync("health", cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task SetBaseUrlAsync(string baseUrl)
    {
        if (!Uri.TryCreate(
                baseUrl.Trim(),
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "عنوان الخادم غير صحيح.",
                nameof(baseUrl));
        }

        var normalized =
            uri.ToString().TrimEnd('/') + "/";

        Preferences.Default.Set(
            BaseUrlKey,
            normalized);

        // HttpClient.BaseAddress cannot be changed after requests
        // have started. Replace the client instead.
        var oldHttp = _http;

        _http = CreateHttpClient(normalized);

        ApplyToken();

        oldHttp.Dispose();

        await Task.CompletedTask;
    }

    public async Task RequestEmailVerificationAsync(
        string email,
        string password,
        string name,
        CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        ValidatePassword(password);
        name = ValidateName(name);

        using var response =
            await _http.PostAsJsonAsync(
                "api/auth/request-email-verification",
                new
                {
                    email,
                    password,
                    name
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task<Account> VerifyEmailAsync(
        string email,
        string code,
        CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        code = code?.Trim() ?? string.Empty;

        if (code.Length != 6 ||
            code.Any(ch => ch < '0' || ch > '9'))
        {
            throw new ArgumentException(
                "رمز التحقق يجب أن يتكون من 6 أرقام.",
                nameof(code));
        }

        using var response =
            await _http.PostAsJsonAsync(
                "api/auth/verify-email",
                new
                {
                    email,
                    code
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return ApplyAuthResponse(
            await response.Content.ReadFromJsonAsync<AuthResponse>(
                cancellationToken: cancellationToken));
    }

    public async Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);

        using var response =
            await _http.PostAsJsonAsync(
                "api/auth/request-password-reset",
                new
                {
                    email
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task<Account> ResetPasswordAsync(
        string email,
        string code,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);

        if (code?.Trim().Length != 6)
        {
            throw new ArgumentException(
                "رمز التحقق يجب أن يتكون من 6 أرقام.",
                nameof(code));
        }

        ValidatePassword(newPassword);

        using var response =
            await _http.PostAsJsonAsync(
                "api/auth/reset-password",
                new
                {
                    email,
                    code = code.Trim(),
                    newPassword
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return ApplyAuthResponse(
            await response.Content.ReadFromJsonAsync<AuthResponse>(
                cancellationToken: cancellationToken));
    }

    public async Task<Account> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        ValidatePassword(password);

        // Login must not be made dependent on a stale/custom server URL that
        // may have been saved by an older build. If the current endpoint cannot
        // be reached, retry exactly once against the known production endpoint.
        // This does not retry credential failures (401/403), so an incorrect
        // password is never masked as a network problem.
        try
        {
            return await LoginAgainstCurrentServerAsync(
                email,
                password,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            const string productionUrl =
                "https://himo-3buh.onrender.com/";

            if (!string.Equals(
                    BaseUrl.TrimEnd('/'),
                    productionUrl.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                await SetBaseUrlAsync(productionUrl);
                return await LoginAgainstCurrentServerAsync(
                    email,
                    password,
                    cancellationToken);
            }

            throw;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string productionUrl =
                "https://himo-3buh.onrender.com/";

            if (!string.Equals(
                    BaseUrl.TrimEnd('/'),
                    productionUrl.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                await SetBaseUrlAsync(productionUrl);
                return await LoginAgainstCurrentServerAsync(
                    email,
                    password,
                    cancellationToken);
            }

            throw new HttpRequestException(
                "تعذر الوصول إلى خادم Himo. تحقق من اتصال الإنترنت وحاول مرة أخرى.");
        }
    }

    private async Task<Account> LoginAgainstCurrentServerAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response =
            await _http.PostAsJsonAsync(
                "api/auth/login-email",
                new
                {
                    email,
                    password
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var result = await response.Content
            .ReadFromJsonAsync<AuthResponse>(
                cancellationToken: cancellationToken);

        return ApplyAuthResponse(result);
    }

    private Account ApplyAuthResponse(AuthResponse? result)
    {
        if (result is null ||
            result.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(result.Token) ||
            string.IsNullOrWhiteSpace(result.Email))
        {
            throw new InvalidOperationException(
                "استجابة تسجيل الدخول غير صالحة.");
        }

        _accessToken = result.Token;

        SecureStorage.Default
            .SetAsync(TokenKey, result.Token)
            .GetAwaiter()
            .GetResult();

        Preferences.Default.Remove(TokenKey);

        ApplyToken();

        return new Account
        {
            UserId = result.UserId,
            Email = result.Email,
            Name = result.Name
        };
    }

    private static string NormalizeEmail(string? value)
    {
        var email =
            value?.Trim().ToLowerInvariant() ??
            string.Empty;

        if (email.Length is < 5 or > 254 ||
            !email.Contains('@') ||
            email.Contains(' '))
        {
            throw new ArgumentException(
                "البريد الإلكتروني غير صحيح.",
                nameof(value));
        }

        var at = email.LastIndexOf('@');

        if (at <= 0 ||
            at == email.Length - 1 ||
            !email[(at + 1)..].Contains('.'))
        {
            throw new ArgumentException(
                "البريد الإلكتروني غير صحيح.",
                nameof(value));
        }

        return email;
    }

    private static void ValidatePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length < 8 ||
            value.Length > 128)
        {
            throw new ArgumentException(
                "كلمة المرور يجب أن تكون بين 8 و128 حرفًا.",
                nameof(value));
        }
    }

    private static string ValidateName(string? value)
    {
        var name =
            value?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 60)
        {
            throw new ArgumentException(
                "الاسم مطلوب وبحد أقصى 60 حرفًا.",
                nameof(value));
        }

        return name;
    }

    public async Task<UserProfileResponse> GetMyProfileAsync(
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.GetAsync(
                "api/me",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<UserProfileResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "استجابة الملف الشخصي غير صالحة.");
    }

    public async Task<UserProfileResponse> UpdateMyProfileAsync(
        string name,
        string status,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.PutAsJsonAsync(
                "api/me",
                new
                {
                    name,
                    status
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<UserProfileResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "استجابة الملف الشخصي غير صالحة.");
    }

    public async Task DeleteAccountAsync(
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.DeleteAsync(
                "api/me",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        ClearToken();
    }

    public async Task LogoutAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (HasToken)
            {
                using var response =
                    await _http.PostAsync(
                        "api/auth/logout",
                        content: null,
                        cancellationToken);
            }
        }
        finally
        {
            ClearToken();
        }
    }

    public async Task LogoutAllAsync(
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.PostAsync(
                "api/auth/logout-all",
                content: null,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        ClearToken();
    }

    public void ClearToken()
    {
        _accessToken = null;

        Preferences.Default.Remove(TokenKey);

        SecureStorage.Default.Remove(TokenKey);

        _http.DefaultRequestHeaders.Authorization = null;
    }

    private async Task InitializeTokenAsync()
    {
        try
        {
            var secureToken =
                await SecureStorage.Default.GetAsync(TokenKey);

            if (!string.IsNullOrWhiteSpace(secureToken))
            {
                _accessToken = secureToken;
                ApplyToken();
                return;
            }

            // One-time migration from the old Preferences location.
            var legacyToken =
                Preferences.Default.Get(
                    TokenKey,
                    string.Empty);

            if (!string.IsNullOrWhiteSpace(legacyToken))
            {
                await SecureStorage.Default.SetAsync(
                    TokenKey,
                    legacyToken);

                Preferences.Default.Remove(TokenKey);

                _accessToken = legacyToken;

                ApplyToken();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Secure token initialization failed: {ex}");
        }
    }

    public async Task RegisterPushTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        using var response =
            await _http.PostAsJsonAsync(
                "api/push-token",
                new
                {
                    token
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task RemovePushTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                "api/push-token")
            {
                Content = JsonContent.Create(
                    new
                    {
                        token
                    })
            };

        using var response =
            await _http.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationDto>>
        GetConversationsAsync(
            CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.GetAsync(
                "api/conversations",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<List<ConversationDto>>(
                cancellationToken: cancellationToken)
            ?? new List<ConversationDto>();
    }

    public async Task ReportConversationAsync(
        Guid conversationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        reason = reason?.Trim() ?? string.Empty;

        if (reason.Length is < 3 or > 200)
        {
            throw new ArgumentException(
                "سبب البلاغ غير صالح.",
                nameof(reason));
        }

        using var response =
            await _http.PostAsJsonAsync(
                $"api/conversations/{conversationId}/report",
                new
                {
                    reason
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task MarkConversationReadAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.PostAsync(
                $"api/conversations/{conversationId}/read",
                content: null,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public Task MarkMessageDeliveredAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
        => MarkMessageDeliveredAsync(messageId.ToString("D"), cancellationToken);

    public async Task MarkMessageDeliveredAsync(
        string? messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId) ||
            !Guid.TryParse(messageId, out var parsedMessageId) ||
            parsedMessageId == Guid.Empty)
            return;

        using var response =
            await _http.PostAsync(
                $"api/messages/{parsedMessageId:D}/delivered",
                content: null,
                cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<ConversationDto> CreateConversationAsync(
        string name,
        CancellationToken cancellationToken = default)
        => CreateConversationAsync(
            name,
            null,
            cancellationToken);

    public async Task<IReadOnlyList<UserSearchDto>>
        SearchUsersAsync(
            string email,
            CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);

        var encoded =
            Uri.EscapeDataString(email);

        using var response =
            await _http.GetAsync(
                $"api/users/search?email={encoded}",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<List<UserSearchDto>>(
                cancellationToken: cancellationToken)
            ?? new List<UserSearchDto>();
    }

    public async Task<ConversationDto>
        CreateConversationAsync(
            string name,
            Guid? userId,
            CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.PostAsJsonAsync(
                "api/conversations",
                new
                {
                    name,
                    userId
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<ConversationDto>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "استجابة الخادم غير صالحة.");
    }

    public async Task<IReadOnlyList<MessageDto>>
        GetMessagesAsync(
            Guid conversationId,
            DateTimeOffset? since = null,
            DateTimeOffset? before = null,
            Guid? beforeId = null,
            int limit = 30,
            CancellationToken cancellationToken = default)
    {
        var path =
            $"api/conversations/{conversationId}/messages";

        limit = Math.Clamp(limit, 1, 100);

        var query =
            new List<string>
            {
                $"limit={limit}"
            };

        if (since.HasValue)
        {
            query.Add(
                $"since={Uri.EscapeDataString(
                    since.Value.ToString("O"))}");
        }

        if (before.HasValue)
        {
            query.Add(
                $"before={Uri.EscapeDataString(
                    before.Value.ToString("O"))}");
        }

        if (beforeId.HasValue)
        {
            query.Add($"beforeId={beforeId.Value:D}");
        }

        if (query.Count > 0)
        {
            path += "?" + string.Join("&", query);
        }

        var timing = Stopwatch.StartNew();
        using var response =
            await _http.GetAsync(
                path,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var result = await response.Content
            .ReadFromJsonAsync<List<MessageDto>>(
                cancellationToken: cancellationToken)
            ?? new List<MessageDto>();

        Debug.WriteLine($"[HimoTiming] GET messages HTTP={(int)response.StatusCode} count={result.Count} elapsed={timing.ElapsedMilliseconds}ms path={path}");
        return result;
    }

    public async Task<MessageDto> SendMessageAsync(
        Guid conversationId,
        string text,
        string? clientMessageId = null,
        Guid? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var attempts = new[]
        {
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(45)
        };

        Exception? lastError = null;

        for (var attempt = 0;
             attempt < attempts.Length;
             attempt++)
        {
            using var timeoutCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            timeoutCts.CancelAfter(
                attempts[attempt]);

            try
            {
                using var response =
                    await _http.PostAsJsonAsync(
                        $"api/conversations/{conversationId}/messages",
                        new
                        {
                            text,
                            clientMessageId,
                            replyToMessageId
                        },
                        timeoutCts.Token);

                await EnsureSuccessAsync(
                    response,
                    timeoutCts.Token);

                return await response.Content
                    .ReadFromJsonAsync<MessageDto>(
                        cancellationToken:
                            timeoutCts.Token)
                    ?? throw new InvalidOperationException(
                        "استجابة الخادم غير صالحة.");
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastError =
                    new TaskCanceledException(
                        "انتهت مهلة إرسال الرسالة.");

                if (attempt ==
                    attempts.Length - 1)
                {
                    throw lastError;
                }

                await RecreateHttpClientForRetryAsync();
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;

                if (!HasToken ||
                    attempt == attempts.Length - 1)
                {
                    throw;
                }

                await RecreateHttpClientForRetryAsync();

                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    cancellationToken);
            }
        }

        throw lastError ??
            new HttpRequestException(
                "تعذر إرسال الرسالة.");
    }

    private async Task RecreateHttpClientForRetryAsync()
    {
        var fresh =
            CreateHttpClient(
                _http.BaseAddress?.ToString()
                ?? GetBaseUrl());

        ApplyToken(fresh, _accessToken);

        var old = _http;

        _http = fresh;

        old.Dispose();

        await Task.CompletedTask;
    }

    public async Task<MessageDto> EditMessageAsync(
        Guid messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.PutAsJsonAsync(
                $"api/messages/{messageId}",
                new
                {
                    text
                },
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<MessageDto>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "استجابة الخادم غير صالحة.");
    }

    public async Task<MessageDto> DeleteMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.DeleteAsync(
                $"api/messages/{messageId}",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<MessageDto>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "استجابة حذف الرسالة غير صالحة.");
    }

    public async Task<MessageDto> UploadAttachmentAsync(
        Guid conversationId,
        FileResult file,
        CancellationToken cancellationToken = default)
    {
        await using var stream =
            await file.OpenReadAsync();

        return await UploadAttachmentAsync(
            conversationId,
            stream,
            file.FileName,
            file.ContentType,
            cancellationToken);
    }

    public async Task<MessageDto> UploadAttachmentAsync(
        Guid conversationId,
        Stream stream,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        using var content =
            new MultipartFormDataContent();

        using var fileContent =
            new StreamContent(stream);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(contentType)
                    ? "application/octet-stream"
                    : contentType);

        content.Add(
            fileContent,
            "file",
            fileName);

        using var response =
            await _http.PostAsync(
                $"api/conversations/{conversationId}/attachments",
                content,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<MessageDto>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "استجابة المرفق غير صالحة.");
    }

    public async Task<string> DownloadAttachmentAsync(
        Guid messageId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _http.GetAsync(
                $"api/messages/{messageId}/attachment",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var safeName =
            Path.GetFileName(fileName);

        var path =
            Path.Combine(
                FileSystem.Current.CacheDirectory,
                $"himo_{messageId:N}_{safeName}");

        await using var source =
            await response.Content
                .ReadAsStreamAsync(cancellationToken);

        await using var destination =
            File.Create(path);

        await source.CopyToAsync(
            destination,
            cancellationToken);

        await destination.FlushAsync(cancellationToken);

        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException("الخادم أعاد ملف مرفق فارغًا.");
        }

        return path;
    }

    private static string NormalizeAndValidatePhone(
        string? value)
    {
        var original =
            value?.Trim() ??
            string.Empty;

        var normalized =
            PhoneNumberUtils.Normalize(original);

        if (!PhoneNumberUtils.IsValid(original))
        {
            throw new ArgumentException(
                "رقم الهاتف غير صحيح.",
                nameof(value));
        }

        return normalized;
    }

    private static string NormalizeDigits(
        string? value)
    {
        var input =
            value ??
            string.Empty;

        var chars =
            new char[input.Length];

        for (var i = 0;
             i < input.Length;
             i++)
        {
            chars[i] =
                input[i] switch
                {
                    >= '٠' and <= '٩' =>
                        (char)('0' +
                               (input[i] - '٠')),

                    >= '۰' and <= '۹' =>
                        (char)('0' +
                               (input[i] - '۰')),

                    _ => input[i]
                };
        }

        return new string(chars);
    }

    private static string GetBaseUrl()
    {
        const string productionUrl =
            "https://himo-3buh.onrender.com/";

        var saved =
            Preferences.Default
                .Get(BaseUrlKey, string.Empty)
                .Trim();

        if (string.IsNullOrWhiteSpace(saved))
        {
            Preferences.Default.Set(
                BaseUrlKey,
                productionUrl);

            return productionUrl;
        }

        return saved.EndsWith('/')
            ? saved
            : saved + "/";
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message =
            await TryReadMessageAsync(
                response,
                cancellationToken);

        if (response.StatusCode ==
                System.Net.HttpStatusCode.Unauthorized &&
            HasToken)
        {
            ClearToken();

            SessionExpired?.Invoke(
                this,
                EventArgs.Empty);

            throw new HttpRequestException(
                "انتهت جلسة تسجيل الدخول. سجّل الدخول مرة أخرى.");
        }

        throw new HttpRequestException(
            message ??
            $"الخادم أعاد الحالة {(int)response.StatusCode}.");
    }

    private static async Task<string?>
        TryReadMessageAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        try
        {
            var body =
                await response.Content
                    .ReadFromJsonAsync<ApiError>(
                        cancellationToken:
                            cancellationToken);

            return body?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record ConversationDto(
        Guid Id,
        string Name,
        string LastMessage,
        DateTimeOffset UpdatedAt,
        int UnreadCount);

    public sealed record MessageDto(
        Guid Id,
        Guid ConversationId,
        Guid SenderUserId,
        string SenderPhoneNumber,
        string Text,
        DateTimeOffset SentAt,
        string? AttachmentFileName = null,
        string? AttachmentContentType = null,
        long? AttachmentSize = null,
        string Status = "sent",
        Guid? ReplyToMessageId = null,
        string? ReplyToText = null,
        bool IsEdited = false,
        string? EditedAt = null,
        bool IsDeleted = false);

    private sealed record AuthResponse(
        string Token,
        Guid UserId,
        string Email,
        string Name);

    public sealed record UserProfileResponse(
        Guid UserId,
        string Email,
        string Name,
        string Status);

    private sealed record RequestCodeResponse(
        string Message,
        string? DevelopmentCode);

    private sealed record ApiError(
        string? Message);
}