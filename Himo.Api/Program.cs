using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Security.Cryptography;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
// Allow the API to run continuously as a Windows Service on the LAN.
builder.Host.UseWindowsService();
// Keep the LAN development default, but allow production/hosting to override
// the listening address through ASPNETCORE_URLS without changing source code.
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(configuredUrls))
    builder.WebHost.UseUrls("http://0.0.0.0:5080");
builder.Services.AddSingleton<PostgresStore>();
builder.Services.AddSingleton<FcmPushService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EmailVerificationService>();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024;
});

// Basic abuse protection for authentication and the API. Keep the policy keyed
// by client IP so one abusive device cannot consume the whole server quota.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("otp-request", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.WebHost.ConfigureKestrel(options =>
{
    // 25 MiB attachment limit plus multipart/request overhead.
    options.Limits.MaxRequestBodySize = 30L * 1024 * 1024;
});

var webApp = builder.Build();

if (!webApp.Environment.IsDevelopment())
{
    // Production must be served over HTTPS by Kestrel/reverse proxy.
    webApp.UseHsts();
    webApp.UseHttpsRedirection();
}

webApp.UseRateLimiter();

// Conservative security headers. HTTPS is enforced in production above.
webApp.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

// Return a consistent JSON response instead of exposing unhandled server exceptions.
webApp.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { message = "حدث خطأ غير متوقع في الخادم." });
    });
});

webApp.MapGet("/", () => Results.Ok(new { status = "ok", service = "Himo.Api", message = "Himo API is running", health = "/health" }));

webApp.MapGet("/health", () =>
{
    // Dependency-free liveness endpoint for Render.
    return Results.Ok(new
    {
        status = "ok",
        service = "Himo.Api",
        databaseCheck = "/health/database"
    });
});

webApp.MapGet("/health/database", () =>
{
    var database = GetDatabaseHealthForHealthEndpoint();
    return Results.Ok(new
    {
        status = database.Connected && database.SchemaReady ? "ok" : "degraded",
        service = "Himo.Api",
        database = database.Connected,
        databaseSchema = database.SchemaReady,
        databaseError = database.Error
    });
});
webApp.MapHub<HimoChatHub>("/hubs/chat");

webApp.MapPost("/api/auth/request-email-verification", async (EmailRegisterRequest request, PostgresStore store, EmailVerificationService emailService, IHostEnvironment environment, CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var name = request.Name?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "الاسم مطلوب." });
    if (name.Length > 60) return Results.BadRequest(new { message = "الاسم طويل جدًا." });
    var password = request.Password ?? string.Empty;
    if (!PasswordRules.IsValid(password)) return Results.BadRequest(new { message = "كلمة المرور يجب أن تكون بين 8 و128 حرفًا." });
    if (store.EmailExists(email)) return Results.Conflict(new { message = "هذا البريد الإلكتروني مستخدم بالفعل." });

    var code = environment.IsDevelopment()
        ? "123456"
        : RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
    if (!store.TrySetEmailVerification(email, name, PasswordRules.Hash(password), code, expiresAt, out var retryAfterSeconds))
        return Results.Json(new { message = $"اطلب رمزًا جديدًا بعد {retryAfterSeconds} ثانية." }, statusCode: StatusCodes.Status429TooManyRequests);

    if (environment.IsDevelopment())
        return Results.Ok(new { message = "تم إنشاء رمز التحقق.", developmentCode = code });

    try
    {
        await emailService.SendCodeAsync(email, name, code, cancellationToken);
        return Results.Ok(new { message = "تم إرسال رمز التحقق إلى بريدك الإلكتروني." });
    }
    catch (EmailProviderNotConfiguredException ex)
    {
        store.RemoveEmailVerification(email);
        return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (EmailProviderException ex)
    {
        store.RemoveEmailVerification(email);
        Console.Error.WriteLine($"[Himo.Email] Brevo HTTP {ex.StatusCode}: {ex.ProviderResponse}");
        var message = ex.StatusCode switch
        {
            400 => "Brevo رفض بيانات البريد. تأكد من أن HIMO_EMAIL_FROM هو المرسل الموثق في Brevo.",
            401 => "مفتاح Brevo غير صالح أو منتهي. حدّث HIMO_EMAIL_API_KEY في Render.",
            403 => "Brevo رفض عملية الإرسال. تأكد من تفعيل الحساب وصلاحية المرسل.",
            429 => "تم تجاوز حد الإرسال في Brevo. حاول لاحقًا.",
            _ => $"تعذر إرسال رمز التحقق عبر Brevo ({ex.StatusCode})."
        };
        return Results.Json(new { message }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        store.RemoveEmailVerification(email);
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch
    {
        store.RemoveEmailVerification(email);
        return Results.Json(new { message = "تعذر إرسال رمز التحقق إلى البريد الآن. حاول مرة أخرى لاحقًا." }, statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("otp-request");

webApp.MapPost("/api/auth/verify-email", (VerifyEmailRequest request, PostgresStore store) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var code = NormalizeDigits(request.Code?.Trim());
    if (code.Length != 6 || code.Any(ch => ch < '0' || ch > '9'))
        return Results.Json(new { message = "رمز التحقق يجب أن يتكون من 6 أرقام." }, statusCode: StatusCodes.Status401Unauthorized);

    if (!store.TryTakeEmailVerification(email, code, out var pending) || pending is null)
        return Results.Json(new { message = "رمز التحقق غير صحيح أو منتهي." }, statusCode: StatusCodes.Status401Unauthorized);

    try
    {
        var user = store.CreateEmailUser(email, pending.Name, pending.PasswordHash, true);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        store.CreateSession(token, user.Id, DateTimeOffset.UtcNow.AddDays(30));
        return Results.Ok(new AuthResponse(token, user.PhoneNumber, user.Name));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
}).RequireRateLimiting("auth");

webApp.MapPost("/api/auth/login-email", (EmailLoginRequest request, PostgresStore store) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var password = request.Password ?? string.Empty;
    if (!PasswordRules.IsValid(password)) return Results.Unauthorized();
    var user = store.AuthenticateEmail(email, password);
    if (user is null) return Results.Json(new { message = "البريد الإلكتروني أو كلمة المرور غير صحيحة." }, statusCode: StatusCodes.Status401Unauthorized);
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    store.CreateSession(token, user.Id, DateTimeOffset.UtcNow.AddDays(30));
    return Results.Ok(new AuthResponse(token, user.PhoneNumber, user.Name));
}).RequireRateLimiting("auth");

webApp.MapPost("/api/auth/request-password-reset", async (PasswordResetRequest request, PostgresStore store, EmailVerificationService emailService, IHostEnvironment environment, CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });

    // Keep the response generic so an unknown email cannot be enumerated.
    if (!store.EmailExists(email))
        return Results.Ok(new { message = "إذا كان البريد مسجلًا في Himo فسيصلك رمز إعادة تعيين." });

    var code = environment.IsDevelopment()
        ? "123456"
        : RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
    if (!store.TrySetPasswordReset(email, code, expiresAt, out var retryAfterSeconds))
        return Results.Json(new { message = $"اطلب رمزًا جديدًا بعد {retryAfterSeconds} ثانية." }, statusCode: StatusCodes.Status429TooManyRequests);

    if (environment.IsDevelopment())
        return Results.Ok(new { message = "تم إنشاء رمز إعادة التعيين.", developmentCode = code });

    try
    {
        await emailService.SendPasswordResetCodeAsync(email, code, cancellationToken);
        return Results.Ok(new { message = "تم إرسال رمز إعادة تعيين كلمة المرور إلى بريدك الإلكتروني." });
    }
    catch (EmailProviderNotConfiguredException ex)
    {
        store.RemovePasswordReset(email);
        return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        store.RemovePasswordReset(email);
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch
    {
        store.RemovePasswordReset(email);
        return Results.Json(new { message = "تعذر إرسال رمز إعادة التعيين الآن. حاول مرة أخرى لاحقًا." }, statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("otp-request");

webApp.MapPost("/api/auth/reset-password", (PasswordResetConfirmRequest request, PostgresStore store) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var code = NormalizeDigits(request.Code?.Trim());
    if (code.Length != 6 || code.Any(ch => ch < '0' || ch > '9'))
        return Results.Json(new { message = "رمز التحقق يجب أن يتكون من 6 أرقام." }, statusCode: StatusCodes.Status401Unauthorized);
    if (!PasswordRules.IsValid(request.NewPassword))
        return Results.BadRequest(new { message = "كلمة المرور يجب أن تكون بين 8 و128 حرفًا." });

    if (!store.TryResetPassword(email, code, PasswordRules.Hash(request.NewPassword!), out var result, out var failure))
    {
        var status = failure == PasswordResetFailure.RateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized;
        var message = failure == PasswordResetFailure.RateLimited
            ? "تم تجاوز عدد المحاولات. اطلب رمزًا جديدًا."
            : "رمز إعادة التعيين غير صحيح أو منتهي.";
        return Results.Json(new { message }, statusCode: status);
    }

    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    store.CreateSession(token, result!.UserId, DateTimeOffset.UtcNow.AddDays(30));
    return Results.Ok(new AuthResponse(token, result.Email, result.Name));
}).RequireRateLimiting("auth");

webApp.MapPost("/api/auth/logout", (HttpRequest http, PostgresStore store) =>
    store.RevokeSession(http) ? Results.NoContent() : Results.Unauthorized());

webApp.MapGet("/api/me", (HttpRequest http, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    return Results.Ok(store.GetProfile(session.UserId));
});

webApp.MapDelete("/api/me", (HttpRequest http, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    store.DeleteAccount(session.UserId);
    return Results.NoContent();
});

webApp.MapPut("/api/me", (HttpRequest http, UpdateProfileRequest request, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var name = request.Name?.Trim() ?? string.Empty;
    var status = request.Status?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "الاسم مطلوب." });
    if (name.Length > 60) return Results.BadRequest(new { message = "الاسم طويل جدًا." });
    if (status.Length > 100) return Results.BadRequest(new { message = "الحالة طويلة جدًا." });
    var cleanStatus = string.IsNullOrWhiteSpace(status) ? "متاح على Himo" : status;
    return Results.Ok(store.UpdateProfile(session.UserId, name, cleanStatus));
});

webApp.MapGet("/api/users/search", (HttpRequest http, string? email, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var normalized = NormalizeEmail(email);
    if (!IsValidEmail(normalized)) return Results.BadRequest(new { message = "أدخل بريدًا إلكترونيًا صحيحًا للبحث." });
    var users = store.SearchUsers(normalized, session.UserId);
    return Results.Ok(users);
});

webApp.MapPost("/api/push-token", (HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] PushTokenRequest request, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var token = request.Token?.Trim() ?? string.Empty;
    if (token.Length < 20 || token.Length > 4096)
        return Results.BadRequest(new { message = "رمز إشعارات الجهاز غير صحيح." });

    store.SavePushToken(session.UserId, token);
    return Results.NoContent();
});

webApp.MapDelete("/api/push-token", (HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] PushTokenRequest request, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var token = request.Token?.Trim() ?? string.Empty;
    if (token.Length == 0) return Results.NoContent();
    store.RemovePushToken(session.UserId, token);
    return Results.NoContent();
});

webApp.MapGet("/api/conversations", (HttpRequest http, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    return Results.Ok(store.GetConversations(session.UserId));
});

webApp.MapPost("/api/conversations/{id:guid}/read", (Guid id, HttpRequest http, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    store.MarkConversationRead(id, session.UserId);
    return Results.NoContent();
});

webApp.MapPost("/api/conversations", (HttpRequest http, CreateConversationRequest request, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var name = request.Name?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "اسم المحادثة مطلوب." });
    if (name.Length > 60) return Results.BadRequest(new { message = "اسم المحادثة طويل جدًا." });
    try
    {
        var conversation = store.CreateConversation(session.UserId, name, request.UserId);
        return Results.Created($"/api/conversations/{conversation.Id}", conversation);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapGet("/api/conversations/{id:guid}/messages", (Guid id, HttpRequest http, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();

    DateTimeOffset? since = null;
    if (http.Query.TryGetValue("since", out var sinceValue) && !string.IsNullOrWhiteSpace(sinceValue))
    {
        if (!DateTimeOffset.TryParse(sinceValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedSince))
            return Results.BadRequest(new { message = "وقت المزامنة غير صحيح." });
        since = parsedSince;
    }

    return Results.Ok(store.GetMessages(id, since));
});

webApp.MapPost("/api/conversations/{id:guid}/messages", async (Guid id, HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] SendMessageRequest request, PostgresStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    var text = request.Text?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest(new { message = "نص الرسالة مطلوب." });
    if (text.Length > 4000) return Results.BadRequest(new { message = "الرسالة طويلة جدًا. الحد الأقصى 4000 حرف." });
    var clientMessageId = request.ClientMessageId?.Trim();
    if (!string.IsNullOrWhiteSpace(clientMessageId))
    {
        if (!Guid.TryParse(clientMessageId, out var parsedClientMessageId))
            return Results.BadRequest(new { message = "معرّف الرسالة غير صحيح." });
        // Store a canonical GUID form so the same client ID is idempotent
        // even if a retry sends different casing or GUID formatting.
        clientMessageId = parsedClientMessageId.ToString("D");
    }
    var message = store.AddMessage(id, session.UserId, session.PhoneNumber, text, clientMessageId);
    var recipientIds = store.GetOtherParticipantUserIds(id, session.UserId);

    // The message is already persisted. Do not make the sender wait for
    // SignalR or Firebase; either notification path can be slow or unavailable.
    // The recipient can still receive the message through normal sync/polling.
    _ = Task.Run(async () =>
    {
        try
        {
            await hub.Clients.Groups(recipientIds.Select(userId => HimoChatHub.UserGroup(userId)))
                .SendAsync("MessageReceived", message, CancellationToken.None);
        }
        catch
        {
            // Realtime delivery is best-effort.
        }

        try
        {
            var tokens = store.GetPushTokens(recipientIds);
            if (tokens.Count > 0)
            {
                var senderName = session.Name;
                var push = webApp.Services.GetRequiredService<FcmPushService>();
                await push.SendMessageAsync(tokens, senderName, message.Text, message.ConversationId, CancellationToken.None);
            }
        }
        catch
        {
            // Push delivery is best-effort.
        }
    });

    // Return the saved message immediately. Notification delivery must never
    // delay or fail the HTTP request used to send the message.
    return Results.Created($"/api/conversations/{id}/messages/{message.Id}", message);
});

webApp.MapPost("/api/conversations/{id:guid}/attachments", async (Guid id, HttpRequest http, PostgresStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    if (!http.HasFormContentType) return Results.BadRequest(new { message = "صيغة الملف غير صحيحة." });

    var form = await http.ReadFormAsync(http.HttpContext.RequestAborted);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length <= 0) return Results.BadRequest(new { message = "لم يتم اختيار ملف." });
    const long maxBytes = 25L * 1024 * 1024;
    if (file.Length > maxBytes) return Results.BadRequest(new { message = "حجم الملف يتجاوز 25 ميجابايت." });

    var originalName = Path.GetFileName(file.FileName);
    if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 180) return Results.BadRequest(new { message = "اسم الملف غير صالح." });
    var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf", "text/plain", "application/zip", "application/octet-stream",
      "audio/mp4", "audio/m4a", "audio/aac", "audio/mpeg", "audio/ogg", "audio/wav" };
    if (!allowed.Contains(contentType)) return Results.BadRequest(new { message = "نوع الملف غير مدعوم حاليًا." });

    var uploads = Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads");
    Directory.CreateDirectory(uploads);
    var messageId = Guid.NewGuid();
    var safeName = new string(originalName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
    var storedName = messageId.ToString("D") + "_" + safeName;
    var fullPath = Path.Combine(uploads, storedName);
    await using (var stream = File.Create(fullPath)) await file.CopyToAsync(stream, http.HttpContext.RequestAborted);

    try
    {
        var displayText = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? $"📷 {originalName}"
            : contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                ? $"🎙️ {originalName}"
                : $"📎 {originalName}";
        var message = store.AddAttachmentMessage(messageId, id, session.UserId, session.PhoneNumber, displayText, originalName, contentType, file.Length);
        var recipientIds = store.GetOtherParticipantUserIds(id, session.UserId);
        await hub.Clients.Groups(recipientIds.Select(userId => HimoChatHub.UserGroup(userId)))
            .SendAsync("MessageReceived", message, http.HttpContext.RequestAborted);
        var tokens = store.GetPushTokens(recipientIds);
        if (tokens.Count > 0)
        {
            var push = webApp.Services.GetRequiredService<FcmPushService>();
            await push.SendMessageAsync(tokens, session.Name, message.Text, message.ConversationId, http.HttpContext.RequestAborted);
        }
        return Results.Created($"/api/conversations/{id}/messages/{message.Id}", message);
    }
    catch
    {
        try { File.Delete(fullPath); } catch { }
        throw;
    }
});

webApp.MapGet("/api/messages/{messageId:guid}/attachment", (Guid messageId, HttpRequest http, PostgresStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var relative = store.GetAttachmentPath(messageId, session.UserId, out var fileName, out var contentType);
    if (relative is null) return Results.NotFound();
    var root = Path.Combine(AppContext.BaseDirectory, "App_Data");
    var full = Path.GetFullPath(Path.Combine(root, relative));
    if (!full.StartsWith(Path.GetFullPath(Path.Combine(root, "uploads")), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return Results.NotFound();
    return Results.File(full, contentType, fileName, enableRangeProcessing: true);
});

webApp.Run();

static string NormalizeDigits(string? value)
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

static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

static bool IsValidEmail(string? value)
{
    var email = NormalizeEmail(value);
    if (email.Length is < 5 or > 254 || email.Contains(' ')) return false;
    var at = email.LastIndexOf('@');
    return at > 0 && at < email.Length - 1 && email[(at + 1)..].Contains('.');
}

static (bool Connected, bool SchemaReady, string? Error) GetDatabaseHealthForHealthEndpoint()
{
    try
    {
        var configured = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("SUPABASE_DB_URL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(configured))
            return (false, false, "DATABASE_URL is not configured.");

        var connectionString = NormalizeHealthConnectionString(configured.Trim());
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using (var ping = connection.CreateCommand())
        {
            ping.CommandText = "SELECT 1;";
            if (Convert.ToInt32(ping.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                return (false, false, "Database ping failed.");
        }

        using var schema = connection.CreateCommand();
        schema.CommandText = @"
SELECT COUNT(*)
FROM information_schema.tables
WHERE table_schema = 'public'
  AND lower(table_name) IN (
    'users',
    'pushtokens',
    'otpcodes',
    'sessions',
    'conversations',
    'messages',
    'conversationparticipants'
  );";
        var count = Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (count != 7)
            return (true, false, $"Database connected, but Himo schema is incomplete ({count}/7 tables).");

        using var columns = connection.CreateCommand();
        columns.CommandText = @"
SELECT table_name, column_name
FROM information_schema.columns
WHERE table_schema = 'public'
  AND (
      (lower(table_name) = 'users' AND lower(column_name) IN ('id','phonenumber','name','status','createdat')) OR
      (lower(table_name) = 'pushtokens' AND lower(column_name) IN ('userid','token','updatedat')) OR
      (lower(table_name) = 'otpcodes' AND lower(column_name) IN ('phonenumber','code','expiresat','failedattempts','lastsentat')) OR
      (lower(table_name) = 'sessions' AND lower(column_name) IN ('token','userid','expiresat')) OR
      (lower(table_name) = 'conversations' AND lower(column_name) IN ('id','owneruserid','name','lastmessage','updatedat')) OR
      (lower(table_name) = 'messages' AND lower(column_name) IN ('id','conversationid','senderuserid','senderphonenumber','text','sentat','clientmessageid','attachmentfilename','attachmentcontenttype','attachmentsize')) OR
      (lower(table_name) = 'conversationparticipants' AND lower(column_name) IN ('conversationid','userid','joinedat','lastreadat'))
  );";
        var requiredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users.id","users.phonenumber","users.name","users.status","users.createdat",
            "pushtokens.userid","pushtokens.token","pushtokens.updatedat",
            "otpcodes.phonenumber","otpcodes.code","otpcodes.expiresat","otpcodes.failedattempts","otpcodes.lastsentat",
            "sessions.token","sessions.userid","sessions.expiresat",
            "conversations.id","conversations.owneruserid","conversations.name","conversations.lastmessage","conversations.updatedat",
            "messages.id","messages.conversationid","messages.senderuserid","messages.senderphonenumber","messages.text","messages.sentat","messages.clientmessageid","messages.attachmentfilename","messages.attachmentcontenttype","messages.attachmentsize",
            "conversationparticipants.conversationid","conversationparticipants.userid","conversationparticipants.joinedat","conversationparticipants.lastreadat"
        };
        using var columnReader = columns.ExecuteReader();
        while (columnReader.Read())
        {
            requiredColumns.Remove($"{columnReader.GetString(0)}.{columnReader.GetString(1)}");
        }
        if (requiredColumns.Count > 0)
            return (true, false, $"Database connected, but required Himo columns are missing: {string.Join(", ", requiredColumns.OrderBy(x => x))}.");

        return (true, true, null);
    }
    catch (Exception ex)
    {
        // Never expose credentials or the complete connection string.
        return (false, false, ex.Message.Length > 240 ? ex.Message[..240] : ex.Message);
    }
}

static string NormalizeHealthConnectionString(string value)
{
    if (!value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        return value;

    return PostgresUriConnectionStringBuilder.Build(value, pooling: false, timeout: 8, commandTimeout: 8);
}





static class PostgresUriConnectionStringBuilder
{
    public static string Build(string value, bool pooling, int timeout, int commandTimeout)
    {
        // Supabase/Render connection URLs can contain URL characters in the password.
        // Do not rely solely on Uri(value), because an unescaped password character can
        // make the URI parser report: "Invalid URI: The hostname could not be parsed."
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            throw new FormatException("Invalid PostgreSQL connection URI.");

        var authorityStart = schemeEnd + 3;
        var pathStart = value.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
        var authority = pathStart >= 0 ? value[authorityStart..pathStart] : value[authorityStart..];
        var pathAndQuery = pathStart >= 0 ? value[pathStart..] : string.Empty;

        // Use the last '@' so an unescaped '@' inside the password does not become the host separator.
        var at = authority.LastIndexOf('@');
        if (at < 0)
            throw new FormatException("PostgreSQL connection URI is missing user information.");

        var userInfo = authority[..at];
        var hostPort = authority[(at + 1)..];
        var colon = userInfo.IndexOf(':');
        var username = colon >= 0 ? userInfo[..colon] : userInfo;
        var password = colon >= 0 ? userInfo[(colon + 1)..] : string.Empty;

        string host;
        int port = 5432;
        if (hostPort.StartsWith("[", StringComparison.Ordinal))
        {
            var close = hostPort.IndexOf(']');
            if (close < 0) throw new FormatException("Invalid PostgreSQL host.");
            host = hostPort[1..close];
            if (hostPort.Length > close + 1)
            {
                if (hostPort[close + 1] != ':' || !int.TryParse(hostPort[(close + 2)..], out port))
                    throw new FormatException("Invalid PostgreSQL port.");
            }
        }
        else
        {
            var lastColon = hostPort.LastIndexOf(':');
            if (lastColon > 0 && int.TryParse(hostPort[(lastColon + 1)..], out var parsedPort))
            {
                host = hostPort[..lastColon];
                port = parsedPort;
            }
            else
            {
                host = hostPort;
            }
        }

        if (string.IsNullOrWhiteSpace(host))
            throw new FormatException("PostgreSQL host is empty.");

        var database = "postgres";
        if (!string.IsNullOrWhiteSpace(pathAndQuery) && pathAndQuery[0] == '/')
        {
            var end = pathAndQuery.IndexOfAny(new[] { '?', '#' });
            var rawDb = end >= 0 ? pathAndQuery[1..end] : pathAndQuery[1..];
            if (!string.IsNullOrWhiteSpace(rawDb))
                database = Uri.UnescapeDataString(rawDb);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Uri.UnescapeDataString(host),
            Port = port,
            Database = database,
            Username = Uri.UnescapeDataString(username),
            Password = Uri.UnescapeDataString(password),
            SslMode = SslMode.Require,
            Pooling = pooling,
            Timeout = timeout,
            CommandTimeout = commandTimeout
        };
        return builder.ConnectionString;
    }
}

sealed class PostgresStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();
    private bool _initialized;

    public PostgresStore(IHostEnvironment environment)
    {
        var configured = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("SUPABASE_DB_URL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("DATABASE_URL لم يتم ضبطه على الخادم.");
        _connectionString = NormalizeConnectionString(configured.Trim());
    }

    private static string NormalizeConnectionString(string value)
    {
        if (!value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            return value;
        return PostgresUriConnectionStringBuilder.Build(value, pooling: true, timeout: 15, commandTimeout: 30);
    }

    public (bool Connected, bool SchemaReady) GetHealthStatus()
    {
        try
        {
        using var connection = OpenRaw();

            using (var ping = connection.CreateCommand())
            {
                ping.CommandText = "SELECT 1;";
                if (Convert.ToInt32(ping.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                    return (false, false);
            }

            using var schema = connection.CreateCommand();
            schema.CommandText = @"
SELECT COUNT(*)
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name IN (
    'users',
    'pushtokens',
    'otpcodes',
    'sessions',
    'conversations',
    'messages',
    'conversationparticipants'
  );";
            var count = Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture);
            return (true, count == 7);
        }
        catch
        {
            return (false, false);
        }
    }

    private NpgsqlConnection Open()
    {
        EnsureInitialized();
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private NpgsqlConnection OpenRaw()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_sync)
        {
            if (_initialized) return;
            Initialize();
            _initialized = true;
        }
    }

    private void Initialize()
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id TEXT PRIMARY KEY NOT NULL,
    PhoneNumber TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'متاح على Himo',
    CreatedAt TEXT NOT NULL
);
ALTER TABLE Users ADD COLUMN IF NOT EXISTS PasswordHash TEXT NULL;
ALTER TABLE Users ADD COLUMN IF NOT EXISTS EmailVerified BOOLEAN NOT NULL DEFAULT FALSE;
CREATE TABLE IF NOT EXISTS PushTokens (
    UserId TEXT NOT NULL,
    Token TEXT NOT NULL UNIQUE,
    UpdatedAt TEXT NOT NULL,
    PRIMARY KEY(UserId, Token),
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_PushTokens_User ON PushTokens(UserId);
CREATE TABLE IF NOT EXISTS OtpCodes (
    PhoneNumber TEXT PRIMARY KEY NOT NULL,
    Code TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    FailedAttempts INTEGER NOT NULL DEFAULT 0,
    LastSentAt TEXT NULL
);
CREATE TABLE IF NOT EXISTS EmailVerificationCodes (
    Email TEXT PRIMARY KEY NOT NULL,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    FailedAttempts INTEGER NOT NULL DEFAULT 0,
    LastSentAt TEXT NULL
);
CREATE TABLE IF NOT EXISTS PasswordResetCodes (
    Email TEXT PRIMARY KEY NOT NULL,
    Code TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    FailedAttempts INTEGER NOT NULL DEFAULT 0,
    LastSentAt TEXT NULL
);
CREATE TABLE IF NOT EXISTS Sessions (
    Token TEXT PRIMARY KEY NOT NULL,
    UserId TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS Conversations (
    Id TEXT PRIMARY KEY NOT NULL,
    OwnerUserId TEXT NOT NULL,
    Name TEXT NOT NULL,
    LastMessage TEXT NOT NULL DEFAULT '',
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY(OwnerUserId) REFERENCES Users(Id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS Messages (
    Id TEXT PRIMARY KEY NOT NULL,
    ConversationId TEXT NOT NULL,
    SenderUserId TEXT NOT NULL,
    SenderPhoneNumber TEXT NOT NULL,
    Text TEXT NOT NULL,
    SentAt TEXT NOT NULL,
    ClientMessageId TEXT NULL,
    AttachmentFileName TEXT NULL,
    AttachmentContentType TEXT NULL,
    AttachmentSize BIGINT NULL,
    FOREIGN KEY(ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE,
    FOREIGN KEY(SenderUserId) REFERENCES Users(Id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS ConversationParticipants (
    ConversationId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    JoinedAt TEXT NOT NULL,
    LastReadAt TEXT NULL,
    PRIMARY KEY(ConversationId, UserId),
    FOREIGN KEY(ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE,
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_Conversations_Owner_Updated ON Conversations(OwnerUserId, UpdatedAt DESC);
CREATE INDEX IF NOT EXISTS IX_Sessions_Expires ON Sessions(ExpiresAt);
CREATE INDEX IF NOT EXISTS IX_Participants_User ON ConversationParticipants(UserId, JoinedAt);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Messages_ClientId ON Messages(ConversationId, SenderUserId, ClientMessageId) WHERE ClientMessageId IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Messages_Conversation_Sent_Id ON Messages(ConversationId, SentAt DESC, Id DESC);
";
        command.ExecuteNonQuery();

        using var backfill = connection.CreateCommand();
        backfill.CommandText = @"
INSERT INTO ConversationParticipants(ConversationId,UserId,JoinedAt)
SELECT c.Id,c.OwnerUserId,c.UpdatedAt FROM Conversations c
WHERE NOT EXISTS (SELECT 1 FROM ConversationParticipants cp WHERE cp.ConversationId=c.Id AND cp.UserId=c.OwnerUserId);";
        backfill.ExecuteNonQuery();
    }

    public object GetProfile(Guid userId)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            command.CommandText = "SELECT PhoneNumber,Name,Status FROM Users WHERE Id=@id LIMIT 1;";
            command.Parameters.AddWithValue("@id", userId.ToString());
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("المستخدم غير موجود.");
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                throw new InvalidOperationException("بيانات الملف الشخصي غير صالحة.");
            return new { email = reader.GetString(0), name = reader.GetString(1), status = reader.GetString(2) };
        }
    }

    public void DeleteAccount(Guid userId)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            // OTP records are intentionally not linked by a foreign key to Users,
            // so remove the account's pending verification record explicitly.
            string? phoneNumber;
            using (var findUser = connection.CreateCommand())
            {
                findUser.Transaction = transaction;
                findUser.CommandText = "SELECT PhoneNumber FROM Users WHERE Id=@id LIMIT 1;";
                findUser.Parameters.AddWithValue("@id", userId.ToString());
                phoneNumber = findUser.ExecuteScalar() as string;
            }

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new InvalidOperationException("المستخدم غير موجود.");

            using (var deleteOtp = connection.CreateCommand())
            {
                deleteOtp.Transaction = transaction;
                deleteOtp.CommandText = "DELETE FROM OtpCodes WHERE PhoneNumber=@phone;";
                deleteOtp.Parameters.AddWithValue("@phone", phoneNumber);
                deleteOtp.ExecuteNonQuery();
            }

            using (var deleteEmailVerification = connection.CreateCommand())
            {
                deleteEmailVerification.Transaction = transaction;
                deleteEmailVerification.CommandText = "DELETE FROM EmailVerificationCodes WHERE lower(Email)=lower(@email);";
                deleteEmailVerification.Parameters.AddWithValue("@email", phoneNumber);
                deleteEmailVerification.ExecuteNonQuery();
            }

            using (var deletePushTokens = connection.CreateCommand())
            {
                deletePushTokens.Transaction = transaction;
                deletePushTokens.CommandText = "DELETE FROM PushTokens WHERE UserId=@user;";
                deletePushTokens.Parameters.AddWithValue("@user", userId.ToString());
                deletePushTokens.ExecuteNonQuery();
            }

            // Clean up explicitly before deleting the user as well as relying on
            // foreign-key cascades. This keeps account deletion correct for databases
            // created by older app versions whose tables may not have had all cascade
            // constraints when they were first created.
            var userIdText = userId.ToString();

            using (var deleteSentMessages = connection.CreateCommand())
            {
                deleteSentMessages.Transaction = transaction;
                deleteSentMessages.CommandText = "DELETE FROM Messages WHERE SenderUserId=@user;";
                deleteSentMessages.Parameters.AddWithValue("@user", userIdText);
                deleteSentMessages.ExecuteNonQuery();
            }

            using (var deleteSessions = connection.CreateCommand())
            {
                deleteSessions.Transaction = transaction;
                deleteSessions.CommandText = "DELETE FROM Sessions WHERE UserId=@user;";
                deleteSessions.Parameters.AddWithValue("@user", userIdText);
                deleteSessions.ExecuteNonQuery();
            }

            using (var deleteParticipants = connection.CreateCommand())
            {
                deleteParticipants.Transaction = transaction;
                deleteParticipants.CommandText = "DELETE FROM ConversationParticipants WHERE UserId=@user;";
                deleteParticipants.Parameters.AddWithValue("@user", userIdText);
                deleteParticipants.ExecuteNonQuery();
            }

            // Remove conversations owned by the deleted account, including their
            // remaining messages and participants, so no private conversation data
            // is left behind when legacy foreign keys are unavailable.
            using (var deleteOwnedMessages = connection.CreateCommand())
            {
                deleteOwnedMessages.Transaction = transaction;
                deleteOwnedMessages.CommandText = @"
DELETE FROM Messages
WHERE ConversationId IN (SELECT Id FROM Conversations WHERE OwnerUserId=@user);";
                deleteOwnedMessages.Parameters.AddWithValue("@user", userIdText);
                deleteOwnedMessages.ExecuteNonQuery();
            }

            using (var deleteOwnedParticipants = connection.CreateCommand())
            {
                deleteOwnedParticipants.Transaction = transaction;
                deleteOwnedParticipants.CommandText = @"
DELETE FROM ConversationParticipants
WHERE ConversationId IN (SELECT Id FROM Conversations WHERE OwnerUserId=@user);";
                deleteOwnedParticipants.Parameters.AddWithValue("@user", userIdText);
                deleteOwnedParticipants.ExecuteNonQuery();
            }

            using (var deleteOwnedConversations = connection.CreateCommand())
            {
                deleteOwnedConversations.Transaction = transaction;
                deleteOwnedConversations.CommandText = "DELETE FROM Conversations WHERE OwnerUserId=@user;";
                deleteOwnedConversations.Parameters.AddWithValue("@user", userIdText);
                deleteOwnedConversations.ExecuteNonQuery();
            }

            using (var deleteUser = connection.CreateCommand())
            {
                deleteUser.Transaction = transaction;
                deleteUser.CommandText = "DELETE FROM Users WHERE Id=@id;";
                deleteUser.Parameters.AddWithValue("@id", userIdText);
                if (deleteUser.ExecuteNonQuery() == 0)
                    throw new InvalidOperationException("المستخدم غير موجود.");
            }

            transaction.Commit();
        }
    }

    public object UpdateProfile(Guid userId, string name, string status)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET Name=@name, Status=@status WHERE Id=@id;";
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@id", userId.ToString());
            if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("المستخدم غير موجود.");
            return GetProfile(userId);
        }
    }

    private static string HashOtp(string code)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hash);
    }

    public bool EmailExists(string email)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM Users WHERE lower(PhoneNumber)=lower(@email) LIMIT 1;";
            command.Parameters.AddWithValue("@email", email);
            return command.ExecuteScalar() is not null;
        }
    }

    public bool TrySetEmailVerification(string email, string name, string passwordHash, string code, DateTimeOffset expiresAt, out int retryAfterSeconds)
    {
        lock (_sync)
        {
            using var connection = Open();
            var now = DateTimeOffset.UtcNow;
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM EmailVerificationCodes WHERE ExpiresAt <= @now;";
            cleanup.Parameters.AddWithValue("@now", now.ToString("O"));
            cleanup.ExecuteNonQuery();

            using var existing = connection.CreateCommand();
            existing.CommandText = "SELECT LastSentAt FROM EmailVerificationCodes WHERE Email=@email LIMIT 1;";
            existing.Parameters.AddWithValue("@email", email);
            var raw = existing.ExecuteScalar();
            if (raw is string lastSentText && DateTimeOffset.TryParse(lastSentText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastSentAt))
            {
                var elapsed = now - lastSentAt;
                if (elapsed < TimeSpan.FromSeconds(60))
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((TimeSpan.FromSeconds(60) - elapsed).TotalSeconds));
                    return false;
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO EmailVerificationCodes(Email,Code,Name,PasswordHash,ExpiresAt,FailedAttempts,LastSentAt)
VALUES(@email,@code,@name,@passwordHash,@expires,0,@sent)
ON CONFLICT(Email) DO UPDATE SET Code=@code,Name=@name,PasswordHash=@passwordHash,ExpiresAt=@expires,FailedAttempts=0,LastSentAt=@sent;";
            command.Parameters.AddWithValue("@email", email);
            command.Parameters.AddWithValue("@code", HashOtp(code));
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@passwordHash", passwordHash);
            command.Parameters.AddWithValue("@expires", expiresAt.ToString("O"));
            command.Parameters.AddWithValue("@sent", now.ToString("O"));
            command.ExecuteNonQuery();
            retryAfterSeconds = 0;
            return true;
        }
    }

    public void RemoveEmailVerification(string email)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM EmailVerificationCodes WHERE Email=@email;";
            command.Parameters.AddWithValue("@email", email);
            command.ExecuteNonQuery();
        }
    }

    public bool TryTakeEmailVerification(string email, string code, out EmailVerificationPending? pending)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT Name,PasswordHash,ExpiresAt,FailedAttempts,Code FROM EmailVerificationCodes WHERE Email=@email;";
            read.Parameters.AddWithValue("@email", email);
            using var reader = read.ExecuteReader();
            if (!reader.Read())
            {
                pending = null;
                transaction.Rollback();
                return false;
            }

            var name = reader.GetString(0);
            var passwordHash = reader.GetString(1);
            var expiresText = reader.GetString(2);
            var failedAttempts = reader.GetInt32(3);
            var storedHash = reader.GetString(4);
            if (!DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow || failedAttempts >= 5)
            {
                reader.Close();
                using var deleteExpired = connection.CreateCommand();
                deleteExpired.Transaction = transaction;
                deleteExpired.CommandText = "DELETE FROM EmailVerificationCodes WHERE Email=@email;";
                deleteExpired.Parameters.AddWithValue("@email", email);
                deleteExpired.ExecuteNonQuery();
                transaction.Commit();
                pending = null;
                return false;
            }

            var suppliedHash = HashOtp(code);
            var storedBytes = Convert.FromHexString(storedHash);
            var suppliedBytes = Convert.FromHexString(suppliedHash);
            if (!CryptographicOperations.FixedTimeEquals(storedBytes, suppliedBytes))
            {
                reader.Close();
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE EmailVerificationCodes SET FailedAttempts=FailedAttempts+1 WHERE Email=@email;";
                update.Parameters.AddWithValue("@email", email);
                update.ExecuteNonQuery();
                transaction.Commit();
                pending = null;
                return false;
            }

            reader.Close();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM EmailVerificationCodes WHERE Email=@email;";
            delete.Parameters.AddWithValue("@email", email);
            delete.ExecuteNonQuery();
            transaction.Commit();
            pending = new EmailVerificationPending(email, name, passwordHash, expiresAt, failedAttempts);
            return true;
        }
    }

    public bool TrySetPasswordReset(string email, string code, DateTimeOffset expiresAt, out int retryAfterSeconds)
    {
        lock (_sync)
        {
            using var connection = Open();
            var now = DateTimeOffset.UtcNow;
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM PasswordResetCodes WHERE ExpiresAt <= @now;";
            cleanup.Parameters.AddWithValue("@now", now.ToString("O"));
            cleanup.ExecuteNonQuery();

            using var existing = connection.CreateCommand();
            existing.CommandText = "SELECT LastSentAt FROM PasswordResetCodes WHERE Email=@email LIMIT 1;";
            existing.Parameters.AddWithValue("@email", email);
            var raw = existing.ExecuteScalar();
            if (raw is string lastSentText && DateTimeOffset.TryParse(lastSentText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastSentAt))
            {
                var elapsed = now - lastSentAt;
                if (elapsed < TimeSpan.FromSeconds(60))
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((TimeSpan.FromSeconds(60) - elapsed).TotalSeconds));
                    return false;
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO PasswordResetCodes(Email,Code,ExpiresAt,FailedAttempts,LastSentAt)
VALUES(@email,@code,@expires,0,@sent)
ON CONFLICT(Email) DO UPDATE SET Code=@code,ExpiresAt=@expires,FailedAttempts=0,LastSentAt=@sent;";
            command.Parameters.AddWithValue("@email", email);
            command.Parameters.AddWithValue("@code", HashOtp(code));
            command.Parameters.AddWithValue("@expires", expiresAt.ToString("O"));
            command.Parameters.AddWithValue("@sent", now.ToString("O"));
            command.ExecuteNonQuery();
            retryAfterSeconds = 0;
            return true;
        }
    }

    public void RemovePasswordReset(string email)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PasswordResetCodes WHERE Email=@email;";
            command.Parameters.AddWithValue("@email", email);
            command.ExecuteNonQuery();
        }
    }

    public bool TryResetPassword(string email, string code, string newPasswordHash, out PasswordResetResult? result, out PasswordResetFailure failure)
    {
        lock (_sync)
        {
            failure = PasswordResetFailure.InvalidCode;
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT ExpiresAt,FailedAttempts,Code FROM PasswordResetCodes WHERE Email=@email;";
            read.Parameters.AddWithValue("@email", email);
            using var reader = read.ExecuteReader();
            if (!reader.Read())
            {
                result = null;
                transaction.Rollback();
                return false;
            }

            var expiresText = reader.GetString(0);
            var failedAttempts = reader.GetInt32(1);
            var storedHash = reader.GetString(2);
            if (!DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
            {
                reader.Close();
                using var deleteExpired = connection.CreateCommand();
                deleteExpired.Transaction = transaction;
                deleteExpired.CommandText = "DELETE FROM PasswordResetCodes WHERE Email=@email;";
                deleteExpired.Parameters.AddWithValue("@email", email);
                deleteExpired.ExecuteNonQuery();
                transaction.Commit();
                result = null;
                return false;
            }

            if (failedAttempts >= 5)
            {
                reader.Close();
                transaction.Rollback();
                failure = PasswordResetFailure.RateLimited;
                result = null;
                return false;
            }

            var suppliedHash = HashOtp(code);
            var storedBytes = Convert.FromHexString(storedHash);
            var suppliedBytes = Convert.FromHexString(suppliedHash);
            if (!CryptographicOperations.FixedTimeEquals(storedBytes, suppliedBytes))
            {
                reader.Close();
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE PasswordResetCodes SET FailedAttempts=FailedAttempts+1 WHERE Email=@email;";
                update.Parameters.AddWithValue("@email", email);
                update.ExecuteNonQuery();
                transaction.Commit();
                result = failedAttempts + 1 >= 5 ? null : null;
                return false;
            }

            reader.Close();
            using var user = connection.CreateCommand();
            user.Transaction = transaction;
            user.CommandText = "SELECT Id,Name FROM Users WHERE lower(PhoneNumber)=lower(@email) LIMIT 1;";
            user.Parameters.AddWithValue("@email", email);
            using var userReader = user.ExecuteReader();
            if (!userReader.Read())
            {
                userReader.Close();
                transaction.Rollback();
                result = null;
                return false;
            }
            var userId = Guid.Parse(userReader.GetString(0));
            var name = userReader.GetString(1);
            userReader.Close();

            using var updateUser = connection.CreateCommand();
            updateUser.Transaction = transaction;
            updateUser.CommandText = "UPDATE Users SET PasswordHash=@hash,EmailVerified=TRUE WHERE Id=@user;";
            updateUser.Parameters.AddWithValue("@hash", newPasswordHash);
            updateUser.Parameters.AddWithValue("@user", userId.ToString());
            updateUser.ExecuteNonQuery();

            using var deleteSessions = connection.CreateCommand();
            deleteSessions.Transaction = transaction;
            deleteSessions.CommandText = "DELETE FROM Sessions WHERE UserId=@user;";
            deleteSessions.Parameters.AddWithValue("@user", userId.ToString());
            deleteSessions.ExecuteNonQuery();

            using var deleteCode = connection.CreateCommand();
            deleteCode.Transaction = transaction;
            deleteCode.CommandText = "DELETE FROM PasswordResetCodes WHERE Email=@email;";
            deleteCode.Parameters.AddWithValue("@email", email);
            deleteCode.ExecuteNonQuery();

            transaction.Commit();
            result = new PasswordResetResult(userId, email, name);
            failure = PasswordResetFailure.None;
            return true;
        }
    }

    public UserRecord CreateEmailUser(string email, string name, string passwordHash, bool emailVerified)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT 1 FROM Users WHERE lower(PhoneNumber)=lower(@email) LIMIT 1;";
            exists.Parameters.AddWithValue("@email", email);
            if (exists.ExecuteScalar() is not null)
                throw new InvalidOperationException("هذا البريد الإلكتروني مستخدم بالفعل.");

            var created = new UserRecord(Guid.NewGuid(), email, name);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO Users(Id,PhoneNumber,Name,CreatedAt,PasswordHash,EmailVerified) VALUES(@id,@email,@name,@created,@hash,@verified);";
            insert.Parameters.AddWithValue("@id", created.Id.ToString());
            insert.Parameters.AddWithValue("@email", email);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
            insert.Parameters.AddWithValue("@hash", passwordHash);
            insert.Parameters.AddWithValue("@verified", emailVerified);
            insert.ExecuteNonQuery();
            transaction.Commit();
            return created;
        }
    }

    public UserRecord? AuthenticateEmail(string email, string password)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,PhoneNumber,Name,PasswordHash,EmailVerified FROM Users WHERE lower(PhoneNumber)=lower(@email) LIMIT 1;";
            command.Parameters.AddWithValue("@email", email);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3)) return null;
            if (!Guid.TryParse(reader.GetString(0), out var id)) return null;
            if (!reader.IsDBNull(4) && !reader.GetBoolean(4))
                throw new InvalidOperationException("يجب التحقق من البريد الإلكتروني قبل تسجيل الدخول.");
            var storedHash = reader.GetString(3);
            if (!PasswordRules.Verify(password, storedHash)) return null;
            return new UserRecord(id, reader.GetString(1), reader.GetString(2));
        }
    }

    public void CreateSession(string token, Guid userId, DateTimeOffset expiresAt)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM Sessions WHERE ExpiresAt <= @now;";
            cleanup.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            cleanup.ExecuteNonQuery();

        using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Sessions(Token,UserId,ExpiresAt) VALUES(@token,@user,@expires);";
            command.Parameters.AddWithValue("@token", token);
            command.Parameters.AddWithValue("@user", userId.ToString());
            command.Parameters.AddWithValue("@expires", expiresAt.ToString("O"));
        command.ExecuteNonQuery();
        }
    }

    public bool RevokeSession(HttpRequest request)
    {
        var token = GetBearerToken(request);
        if (token is null) return false;
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Sessions WHERE Token=@token;";
            command.Parameters.AddWithValue("@token", token);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public bool TryGetSession(HttpRequest request, out SessionRecord? session)
    {
        session = null;
        var token = GetBearerToken(request);
        if (token is null) return false;
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT s.UserId,u.PhoneNumber,u.Name,s.ExpiresAt FROM Sessions s LEFT JOIN Users u ON u.Id=s.UserId WHERE s.Token=@token;";
            command.Parameters.AddWithValue("@token", token);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            var tokenValue = token;
            if (!DateTimeOffset.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires))
            {
                // Treat a malformed persisted expiry as an invalid session rather
                // than allowing a corrupt database value to turn authentication
                // requests into HTTP 500 errors.
                reader.Close();
                using var deleteMalformed = connection.CreateCommand();
                deleteMalformed.CommandText = "DELETE FROM Sessions WHERE Token=@token;";
                deleteMalformed.Parameters.AddWithValue("@token", tokenValue);
                deleteMalformed.ExecuteNonQuery();
                return false;
            }
            if (expires <= DateTimeOffset.UtcNow)
            {
                // Remove the expired session immediately so stale bearer tokens do not
                // accumulate in the SQLite database.
                reader.Close();
                using var delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM Sessions WHERE Token=@token;";
                delete.Parameters.AddWithValue("@token", tokenValue);
                delete.ExecuteNonQuery();
                return false;
            }
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
                !Guid.TryParse(reader.GetString(0), out var userId))
            {
                // A malformed or orphaned persisted session must not turn authentication
                // into HTTP 500. Remove the invalid session and require login.
                reader.Close();
                using var deleteMalformedUser = connection.CreateCommand();
                deleteMalformedUser.CommandText = "DELETE FROM Sessions WHERE Token=@token;";
                deleteMalformedUser.Parameters.AddWithValue("@token", tokenValue);
                deleteMalformedUser.ExecuteNonQuery();
                return false;
            }

            session = new SessionRecord(userId, reader.GetString(1), reader.GetString(2), expires);
            return true;
        }
    }

    public void SavePushToken(Guid userId, string token)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO PushTokens(UserId,Token,UpdatedAt)
VALUES(@user,@token,@updated)
ON CONFLICT(UserId,Token) DO UPDATE SET UpdatedAt=@updated;";
            command.Parameters.AddWithValue("@user", userId.ToString());
            command.Parameters.AddWithValue("@token", token);
            command.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void RemovePushToken(Guid userId, string token)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PushTokens WHERE UserId=@user AND Token=@token;";
            command.Parameters.AddWithValue("@user", userId.ToString());
            command.Parameters.AddWithValue("@token", token);
            command.ExecuteNonQuery();
        }
    }

    public void RemovePushToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PushTokens WHERE Token=@token;";
            command.Parameters.AddWithValue("@token", token);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<string> GetPushTokens(IReadOnlyList<Guid> userIds)
    {
        if (userIds.Count == 0) return Array.Empty<string>();

        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var parameterNames = new List<string>(userIds.Count);
            for (var i = 0; i < userIds.Count; i++)
            {
                var name = "@u" + i;
                parameterNames.Add(name);
                command.Parameters.AddWithValue(name, userIds[i].ToString());
            }

            command.CommandText = $"SELECT Token FROM PushTokens WHERE UserId IN ({string.Join(',', parameterNames)});";
            using var reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    var token = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(token)) result.Add(token);
                }
            }
            return result;
        }
    }

    public IReadOnlyList<Guid> GetOtherParticipantUserIds(Guid conversationId, Guid currentUserId)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT UserId
FROM ConversationParticipants
WHERE ConversationId=@conversation AND UserId<>@user;";
            command.Parameters.AddWithValue("@conversation", conversationId.ToString());
            command.Parameters.AddWithValue("@user", currentUserId.ToString());
            using var reader = command.ExecuteReader();
            var result = new List<Guid>();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0) && Guid.TryParse(reader.GetString(0), out var userId))
                    result.Add(userId);
            }
            return result;
        }
    }

    public IReadOnlyList<ConversationDto> GetConversations(Guid userId)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT c.Id,
       CASE
           WHEN (SELECT COUNT(*) FROM ConversationParticipants cp WHERE cp.ConversationId=c.Id)=2
                THEN COALESCE((SELECT u.Name
                               FROM ConversationParticipants cp2
                               JOIN Users u ON u.Id=cp2.UserId
                               WHERE cp2.ConversationId=c.Id AND cp2.UserId<>@user
                               LIMIT 1), c.Name)
           ELSE c.Name
       END AS DisplayName,
       c.LastMessage,
       c.UpdatedAt,
       (SELECT COUNT(*)
        FROM Messages m
        WHERE m.ConversationId=c.Id
          AND m.SenderUserId<>@user
          AND m.SentAt > COALESCE(p.LastReadAt, p.JoinedAt)) AS UnreadCount
FROM Conversations c
JOIN ConversationParticipants p ON p.ConversationId=c.Id
WHERE p.UserId=@user
ORDER BY c.UpdatedAt DESC;";
            command.Parameters.AddWithValue("@user", userId.ToString());
            using var reader = command.ExecuteReader();
            var result = new List<ConversationDto>();
            while (reader.Read())
            {
                if (!Guid.TryParse(reader.GetString(0), out var id) ||
                    reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(4) ||
                    !DateTimeOffset.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
                    continue;
                result.Add(new ConversationDto(id, reader.GetString(1), reader.GetString(2), updatedAt, reader.GetInt32(4)));
            }
            return result;
        }
    }

    public IReadOnlyList<UserSearchDto> SearchUsers(string email, Guid currentUserId)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,PhoneNumber,Name FROM Users WHERE lower(PhoneNumber) LIKE lower(@email) || '%' AND Id <> @current ORDER BY PhoneNumber LIMIT 20;";
            command.Parameters.AddWithValue("@email", email);
            command.Parameters.AddWithValue("@current", currentUserId.ToString());
            using var reader = command.ExecuteReader();
            var result = new List<UserSearchDto>();
            while (reader.Read())
            {
                if (!Guid.TryParse(reader.GetString(0), out var userId) ||
                    reader.IsDBNull(1) || reader.IsDBNull(2))
                    continue;

                result.Add(new UserSearchDto(userId, reader.GetString(1), reader.GetString(2)));
            }
            return result;
        }
    }

    public ConversationDto CreateConversation(Guid userId, string name, Guid? participantUserId = null)
    {
        lock (_sync)
        {
            using var connection = Open();

            if (participantUserId.HasValue)
            {
                if (participantUserId.Value == userId)
                    throw new InvalidOperationException("لا يمكن بدء محادثة مع حسابك.");

                using var existsUser = connection.CreateCommand();
                existsUser.CommandText = "SELECT 1 FROM Users WHERE Id=@id LIMIT 1;";
                existsUser.Parameters.AddWithValue("@id", participantUserId.Value.ToString());
                if (existsUser.ExecuteScalar() is null)
                    throw new InvalidOperationException("المستخدم غير موجود.");

                using var existing = connection.CreateCommand();
                existing.CommandText = @"
SELECT c.Id,
       c.Name,
       c.LastMessage,
       c.UpdatedAt,
       (SELECT COUNT(*)
        FROM Messages m
        WHERE m.ConversationId=c.Id
          AND m.SenderUserId<>@owner
          AND m.SentAt > COALESCE(pOwner.LastReadAt, pOwner.JoinedAt)) AS UnreadCount
FROM Conversations c
JOIN ConversationParticipants pOwner
  ON pOwner.ConversationId=c.Id AND pOwner.UserId=@owner
WHERE EXISTS (SELECT 1 FROM ConversationParticipants p1 WHERE p1.ConversationId=c.Id AND p1.UserId=@owner)
  AND EXISTS (SELECT 1 FROM ConversationParticipants p2 WHERE p2.ConversationId=c.Id AND p2.UserId=@participant)
  AND (SELECT COUNT(*) FROM ConversationParticipants p3 WHERE p3.ConversationId=c.Id)=2
ORDER BY c.UpdatedAt DESC
LIMIT 1;";
                existing.Parameters.AddWithValue("@owner", userId.ToString());
                existing.Parameters.AddWithValue("@participant", participantUserId.Value.ToString());
                using (var existingReader = existing.ExecuteReader())
                {
                    if (existingReader.Read())
                    {
                        if (existingReader.IsDBNull(0) || existingReader.IsDBNull(1) || existingReader.IsDBNull(2) ||
                            existingReader.IsDBNull(3) || existingReader.IsDBNull(4) ||
                            !Guid.TryParse(existingReader.GetString(0), out var existingId) ||
                            !DateTimeOffset.TryParse(existingReader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var existingUpdatedAt) ||
                            !int.TryParse(existingReader.GetValue(4)?.ToString(), out var existingUnreadCount))
                            throw new InvalidOperationException("بيانات المحادثة الموجودة غير صالحة.");
                        return new ConversationDto(existingId, existingReader.GetString(1), existingReader.GetString(2), existingUpdatedAt, existingUnreadCount);
                    }
                }
            }

            var conversation = new ConversationDto(Guid.NewGuid(), name, "", DateTimeOffset.UtcNow, 0);
            using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Conversations(Id,OwnerUserId,Name,LastMessage,UpdatedAt) VALUES(@id,@user,@name,'',@updated);";
            command.Parameters.AddWithValue("@id", conversation.Id.ToString());
            command.Parameters.AddWithValue("@user", userId.ToString());
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@updated", conversation.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();

            using var owner = connection.CreateCommand();
            owner.Transaction = transaction;
            owner.CommandText = "INSERT INTO ConversationParticipants(ConversationId,UserId,JoinedAt,LastReadAt) VALUES(@conversation,@user,@joined,@read);";
            owner.Parameters.AddWithValue("@conversation", conversation.Id.ToString());
            owner.Parameters.AddWithValue("@user", userId.ToString());
            owner.Parameters.AddWithValue("@joined", conversation.UpdatedAt.ToString("O"));
            owner.Parameters.AddWithValue("@read", conversation.UpdatedAt.ToString("O"));
            owner.ExecuteNonQuery();

            if (participantUserId.HasValue && participantUserId.Value != userId)
            {
                using var participant = connection.CreateCommand();
                participant.Transaction = transaction;
                participant.CommandText = "INSERT INTO ConversationParticipants(ConversationId,UserId,JoinedAt,LastReadAt) VALUES(@conversation,@user,@joined,@read) ON CONFLICT(ConversationId,UserId) DO NOTHING;";
                participant.Parameters.AddWithValue("@conversation", conversation.Id.ToString());
                participant.Parameters.AddWithValue("@user", participantUserId.Value.ToString());
                participant.Parameters.AddWithValue("@joined", conversation.UpdatedAt.ToString("O"));
                participant.Parameters.AddWithValue("@read", conversation.UpdatedAt.ToString("O"));
                participant.ExecuteNonQuery();
            }
            transaction.Commit();
            return conversation;
        }
    }

    public bool ExistsConversation(Guid id, Guid userId)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM ConversationParticipants WHERE ConversationId=@id AND UserId=@user LIMIT 1;";
            command.Parameters.AddWithValue("@id", id.ToString());
            command.Parameters.AddWithValue("@user", userId.ToString());
            return command.ExecuteScalar() is not null;
        }
    }

    public void MarkConversationRead(Guid conversationId, Guid userId)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ConversationParticipants SET LastReadAt=@read WHERE ConversationId=@conversation AND UserId=@user;";
            command.Parameters.AddWithValue("@read", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("@conversation", conversationId.ToString());
            command.Parameters.AddWithValue("@user", userId.ToString());
        command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<MessageDto> GetMessages(Guid id, DateTimeOffset? since = null)
    {
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
            // The initial load returns the most recent 500 messages. Incremental
            // syncs return only messages at or after the supplied timestamp; the
            // client deduplicates the boundary message using its stable server ID.
            command.CommandText = since.HasValue
                ? "SELECT Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,AttachmentFileName,AttachmentContentType,AttachmentSize FROM Messages WHERE ConversationId=@id AND SentAt >= @since ORDER BY SentAt ASC, Id ASC LIMIT 500;"
                : "SELECT Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,AttachmentFileName,AttachmentContentType,AttachmentSize FROM Messages WHERE ConversationId=@id ORDER BY SentAt DESC, Id DESC LIMIT 500;";
            command.Parameters.AddWithValue("@id", id.ToString());
            if (since.HasValue)
                command.Parameters.AddWithValue("@since", since.Value.ToString("O"));
            using var reader = command.ExecuteReader();
            var result = new List<MessageDto>();
            while (reader.Read())
            {
                if (!Guid.TryParse(reader.GetString(0), out var messageId) ||
                    !Guid.TryParse(reader.GetString(1), out var conversationId) ||
                    !Guid.TryParse(reader.GetString(2), out var senderId) ||
                    reader.IsDBNull(3) || reader.IsDBNull(4) ||
                    !DateTimeOffset.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sentAt))
                    continue;
                result.Add(new MessageDto(messageId, conversationId, senderId, reader.GetString(3), reader.GetString(4), sentAt, reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt64(8)));
            }
            if (!since.HasValue)
                result.Reverse();
            return result;
        }
    }

    public MessageDto AddMessage(Guid conversationId, Guid userId, string senderPhone, string text, string? clientMessageId)
    {
        lock (_sync)
        {
            using var connection = Open();
            if (!string.IsNullOrWhiteSpace(clientMessageId))
            {
                using var existing = connection.CreateCommand();
                existing.CommandText = "SELECT Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,AttachmentFileName,AttachmentContentType,AttachmentSize FROM Messages WHERE ConversationId=@conversation AND SenderUserId=@user AND ClientMessageId=@client LIMIT 1;";
                existing.Parameters.AddWithValue("@conversation", conversationId.ToString());
                existing.Parameters.AddWithValue("@user", userId.ToString());
                existing.Parameters.AddWithValue("@client", clientMessageId);
                using (var reader = existing.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (!Guid.TryParse(reader.GetString(0), out var existingId) ||
                            !Guid.TryParse(reader.GetString(1), out var existingConversationId) ||
                            !Guid.TryParse(reader.GetString(2), out var existingSenderId) ||
                            reader.IsDBNull(3) || reader.IsDBNull(4) ||
                            !DateTimeOffset.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var existingSentAt))
                        {
                            throw new InvalidOperationException("بيانات الرسالة الموجودة غير صالحة.");
                        }

                        return new MessageDto(existingId, existingConversationId, existingSenderId, reader.GetString(3), reader.GetString(4), existingSentAt, reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt64(8));
                    }
                }
            }

            var message = new MessageDto(Guid.NewGuid(), conversationId, userId, senderPhone, text, DateTimeOffset.UtcNow);
            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = string.IsNullOrWhiteSpace(clientMessageId)
                ? "INSERT INTO Messages(Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,ClientMessageId) VALUES(@id,@conversation,@user,@phone,@text,@sent,@client);"
                : "INSERT INTO Messages(Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,ClientMessageId) VALUES(@id,@conversation,@user,@phone,@text,@sent,@client) ON CONFLICT DO NOTHING;";
            insert.Parameters.AddWithValue("@id", message.Id.ToString());
            insert.Parameters.AddWithValue("@conversation", conversationId.ToString());
            insert.Parameters.AddWithValue("@user", userId.ToString());
            insert.Parameters.AddWithValue("@phone", senderPhone);
            insert.Parameters.AddWithValue("@text", text);
            insert.Parameters.AddWithValue("@sent", message.SentAt.ToString("O"));
            insert.Parameters.AddWithValue("@client", (object?)clientMessageId ?? DBNull.Value);
            var inserted = insert.ExecuteNonQuery();
            if (inserted == 0 && !string.IsNullOrWhiteSpace(clientMessageId))
            {
                transaction.Rollback();
                using var existingAfterRace = connection.CreateCommand();
                existingAfterRace.CommandText = "SELECT Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,AttachmentFileName,AttachmentContentType,AttachmentSize FROM Messages WHERE ConversationId=@conversation AND SenderUserId=@user AND ClientMessageId=@client LIMIT 1;";
                existingAfterRace.Parameters.AddWithValue("@conversation", conversationId.ToString());
                existingAfterRace.Parameters.AddWithValue("@user", userId.ToString());
                existingAfterRace.Parameters.AddWithValue("@client", clientMessageId);
                using var existingReader = existingAfterRace.ExecuteReader();
                if (existingReader.Read())
                {
                    if (!Guid.TryParse(existingReader.GetString(0), out var racedId) ||
                        !Guid.TryParse(existingReader.GetString(1), out var racedConversationId) ||
                        !Guid.TryParse(existingReader.GetString(2), out var racedSenderId) ||
                        !DateTimeOffset.TryParse(existingReader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var racedSentAt))
                        throw new InvalidOperationException("بيانات الرسالة الموجودة غير صالحة.");
                    return new MessageDto(racedId, racedConversationId, racedSenderId, existingReader.GetString(3), existingReader.GetString(4), racedSentAt, existingReader.IsDBNull(6) ? null : existingReader.GetString(6), existingReader.IsDBNull(7) ? null : existingReader.GetString(7), existingReader.IsDBNull(8) ? null : existingReader.GetInt64(8));
                }
                throw new InvalidOperationException("تعذر حفظ الرسالة.");
            }
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Conversations SET LastMessage=@text, UpdatedAt=@updated WHERE Id=@id;";
            update.Parameters.AddWithValue("@text", text);
            update.Parameters.AddWithValue("@updated", message.SentAt.ToString("O"));
            update.Parameters.AddWithValue("@id", conversationId.ToString());
            update.ExecuteNonQuery();
            transaction.Commit();
            return message;
        }
    }

    public MessageDto AddAttachmentMessage(Guid messageId, Guid conversationId, Guid userId, string senderPhone, string displayText, string fileName, string contentType, long size)
    {
        lock (_sync)
        {
            using var connection = Open();
            var message = new MessageDto(messageId, conversationId, userId, senderPhone, displayText, DateTimeOffset.UtcNow, fileName, contentType, size);
            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO Messages(Id,ConversationId,SenderUserId,SenderPhoneNumber,Text,SentAt,ClientMessageId,AttachmentFileName,AttachmentContentType,AttachmentSize) VALUES(@id,@conversation,@user,@phone,@text,@sent,NULL,@file,@type,@size);";
            insert.Parameters.AddWithValue("@id", message.Id.ToString());
            insert.Parameters.AddWithValue("@conversation", conversationId.ToString());
            insert.Parameters.AddWithValue("@user", userId.ToString());
            insert.Parameters.AddWithValue("@phone", senderPhone);
            insert.Parameters.AddWithValue("@text", displayText);
            insert.Parameters.AddWithValue("@sent", message.SentAt.ToString("O"));
            insert.Parameters.AddWithValue("@file", fileName);
            insert.Parameters.AddWithValue("@type", contentType);
            insert.Parameters.AddWithValue("@size", size);
            insert.ExecuteNonQuery();

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Conversations SET LastMessage=@text, UpdatedAt=@updated WHERE Id=@id;";
            update.Parameters.AddWithValue("@text", displayText);
            update.Parameters.AddWithValue("@updated", message.SentAt.ToString("O"));
            update.Parameters.AddWithValue("@id", conversationId.ToString());
            update.ExecuteNonQuery();
            transaction.Commit();
            return message;
        }
    }

    public string? GetAttachmentPath(Guid messageId, Guid userId, out string fileName, out string contentType)
    {
        fileName = string.Empty;
        contentType = "application/octet-stream";
        lock (_sync)
        {
            using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT m.AttachmentFileName,m.AttachmentContentType,c.Id
FROM Messages m JOIN ConversationParticipants c ON c.ConversationId=m.ConversationId
WHERE m.Id=@id AND c.UserId=@user AND m.AttachmentFileName IS NOT NULL LIMIT 1;";
            command.Parameters.AddWithValue("@id", messageId.ToString());
            command.Parameters.AddWithValue("@user", userId.ToString());
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            fileName = reader.GetString(0);
            if (!reader.IsDBNull(1)) contentType = reader.GetString(1);
            return Path.Combine("uploads", messageId.ToString("D") + "_" + SanitizeFileName(fileName));
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static string? GetBearerToken(HttpRequest request)
    {
        var value = request.Headers.TryGetValue("Authorization", out var header)
            ? header.ToString()
            : request.Query.TryGetValue("access_token", out var queryToken) ? $"Bearer {queryToken}" : string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var token = value[7..].Trim();
        // Sessions are generated from 32 random bytes and stored as 64 hex
        // characters. Reject malformed bearer values before touching SQLite.
        if (token.Length != 64) return null;
        foreach (var ch in token)
        {
            var isHex = (ch >= '0' && ch <= '9')
                || (ch >= 'A' && ch <= 'F')
                || (ch >= 'a' && ch <= 'f');
            if (!isHex) return null;
        }

        return token;
    }
}


sealed class HimoChatHub : Hub
{
    private readonly PostgresStore _store;
    public HimoChatHub(PostgresStore store) => _store = store;

    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        if (http is null || !_store.TryGetSession(http.Request, out var session) || session is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(session.UserId));
        await base.OnConnectedAsync();
    }
}

static class PasswordRules
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static bool IsValid(string? password) => !string.IsNullOrWhiteSpace(password) && password.Length is >= 8 and <= 128;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}

record EmailRegisterRequest(string? Email, string? Password, string? Name);
record EmailLoginRequest(string? Email, string? Password);
record VerifyEmailRequest(string? Email, string? Code);
record PasswordResetRequest(string? Email);
record PasswordResetConfirmRequest(string? Email, string? Code, string? NewPassword);
record PasswordResetResult(Guid UserId, string Email, string Name);
enum PasswordResetFailure { None, InvalidCode, RateLimited }
record PushTokenRequest(string? Token);
record UpdateProfileRequest(string? Name, string? Status);
record CreateConversationRequest(string? Name, Guid? UserId);
record SendMessageRequest(string? Text, string? ClientMessageId);
record AuthResponse(string Token, string Email, string Name);
record UserRecord(Guid Id, string PhoneNumber, string Name);
record EmailVerificationPending(string Email, string Name, string PasswordHash, DateTimeOffset ExpiresAt, int FailedAttempts);
record SessionRecord(Guid UserId, string PhoneNumber, string Name, DateTimeOffset ExpiresAt);
sealed class ConversationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string LastMessage { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int UnreadCount { get; init; }

    public ConversationDto()
    {
        Name = string.Empty;
        LastMessage = string.Empty;
    }

    public ConversationDto(Guid id, string name, string lastMessage, DateTimeOffset updatedAt, int unreadCount)
    {
        Id = id;
        Name = name ?? string.Empty;
        LastMessage = lastMessage ?? string.Empty;
        UpdatedAt = updatedAt;
        UnreadCount = unreadCount;
    }
}
record MessageDto(Guid Id, Guid ConversationId, Guid SenderUserId, string SenderPhoneNumber, string Text, DateTimeOffset SentAt, string? AttachmentFileName = null, string? AttachmentContentType = null, long? AttachmentSize = null);
record UserSearchDto(Guid Id, string Email, string Name);
