using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using QRCoder;

// Bump these version identifiers whenever the corresponding legal text changes.
const string CurrentTermsVersion = "terms-2026-10-18-v1";
const string CurrentPrivacyVersion = "privacy-2026-10-18-v1";

var builder = WebApplication.CreateBuilder(args);
// Emit clear container startup diagnostics to stdout (visible in Render logs).
// Never write connection strings, API keys, or other secret values to the log.
var renderPort = Environment.GetEnvironmentVariable("PORT");
Console.WriteLine($"[HimoStartup] Process starting. Environment={builder.Environment.EnvironmentName}; PORT={(string.IsNullOrWhiteSpace(renderPort) ? "(not set)" : renderPort)}; ASPNETCORE_URLS={(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "(not set)")}");
// Allow the API to run continuously as a Windows Service on the LAN.
builder.Host.UseWindowsService();
// Render injects PORT (normally 10000). Prefer that value so the app listens
// on the port expected by Render. On local machines, preserve ASPNETCORE_URLS
// when supplied and otherwise use the documented LAN development default.
if (int.TryParse(renderPort, out var port) && port is > 0 and <= 65535)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://0.0.0.0:5080");
// Render terminates TLS at its edge proxy. Honor forwarded scheme/client IP
// before HTTPS redirection and IP-based rate limiting. Render proxy addresses
// are dynamic, so the app is intended to be reachable only through Render's edge.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<MongoStore>();
builder.Services.AddHostedService<ExpiredMessagesCleanupService>();
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
    options.AddPolicy("web-pair-start", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("web-pair-poll", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 90,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("device-pair-approve", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.WebHost.ConfigureKestrel(options =>
{
    // 100 MiB video attachment limit plus multipart/request overhead.
    options.Limits.MaxRequestBodySize = 110L * 1024 * 1024;
});

var webApp = builder.Build();
webApp.UseForwardedHeaders();

// Render terminates HTTPS at its edge proxy. Keep liveness independent from
// MongoDB, Firebase, rate limiting, and HTTPS redirect middleware. This short
// circuit also prevents a forwarded-proto mismatch from breaking Render's
// health probe while the application is otherwise starting.
webApp.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) &&
        string.Equals(context.Request.Path.Value, "/health", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            "{\"status\":\"ok\",\"service\":\"Himo.Api\",\"databaseCheck\":\"/health/database\"}",
            context.RequestAborted);
        return;
    }

    await next();
});

webApp.Lifetime.ApplicationStarted.Register(() =>
    webApp.Logger.LogInformation("[HimoStartup] Himo.Api is ready and listening. Environment={Environment}; URLs={Urls}",
        webApp.Environment.EnvironmentName, string.Join(",", webApp.Urls)));
webApp.Lifetime.ApplicationStopping.Register(() =>
    webApp.Logger.LogWarning("[HimoStartup] Himo.Api is stopping."));

// Server-side timing for message requests; use Render logs to separate API/DB time from client/network time.
webApp.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isMessageApi = path.StartsWith("/api/conversations/", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsPost(context.Request.Method));
    if (!isMessageApi)
    {
        await next();
        return;
    }

    var timing = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        Console.WriteLine($"[HimoServerTiming] method={context.Request.Method} path={path}{context.Request.QueryString} status={context.Response.StatusCode} elapsed={timing.ElapsedMilliseconds}ms trace={context.TraceIdentifier}");
    }
});

webApp.MapGet("/health/push", (FcmPushService push, MongoStore store) =>
{
    return Results.Ok(new
    {
        status = push.IsEnabled ? "ok" : "disabled",
        firebaseMessaging = push.IsEnabled,
        projectId = push.ProjectId,
        registeredTokens = store.GetPushTokenCount()
    });
});

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

    if (context.Request.Path.StartsWithSegments("/web", StringComparison.OrdinalIgnoreCase))
    {
        // Himo Web must execute only same-origin scripts. QR images are generated
        // by the API itself; no CDN script can read the browser's stored session token.
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; " +
            "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    }

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

// Serve the browser client from Himo.Api/wwwroot/web after common security headers.
webApp.UseDefaultFiles();
webApp.UseStaticFiles();

webApp.MapGet("/", () => Results.Ok(new { status = "ok", service = "Himo.Api", message = "Himo API is running", health = "/health" }));
webApp.MapGet("/web", () => Results.Redirect("/web/"));

// Android Verified App Links association. Configure one or more SHA-256 signing
// certificate fingerprints in Render's ANDROID_APP_SHA256_CERT_FINGERPRINTS
// environment variable, separated by commas. Include both Debug and Release
// certificates only when both app variants should open links directly.
webApp.MapGet("/.well-known/assetlinks.json", (IConfiguration configuration) =>
{
    var fingerprints = (configuration["ANDROID_APP_SHA256_CERT_FINGERPRINTS"] ?? string.Empty)
        .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value =>
        {
            var candidate = value.Trim();
            var labelSeparator = candidate.IndexOf(':');
            if (labelSeparator > 0)
            {
                var label = candidate[..labelSeparator].Replace("-", string.Empty, StringComparison.Ordinal);
                if (label.Equals("SHA256", StringComparison.OrdinalIgnoreCase))
                    candidate = candidate[(labelSeparator + 1)..];
            }

            // Accept keytool's "SHA256: AA:BB:..." line as well as a raw fingerprint.
            var compact = new string(candidate.Where(Uri.IsHexDigit).ToArray());
            if (compact.Length != 64)
                return string.Empty;

            return string.Join(":", Enumerable.Range(0, 32)
                .Select(index => compact.Substring(index * 2, 2))).ToUpperInvariant();
        })
        .Where(value => value.Length == 95)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    if (fingerprints.Length == 0)
        return Results.Json(Array.Empty<object>());

    return Results.Json(new[]
    {
        new
        {
            relation = new[] { "delegate_permission/common.handle_all_urls" },
            target = new
            {
                @namespace = "android_app",
                package_name = "com.companyname.himo",
                sha256_cert_fingerprints = fingerprints
            }
        }
    });
});

// WhatsApp and many other messaging clients reliably recognize HTTPS links,
// but may not treat custom schemes (himo://) as tappable links. This landing
// page converts an HTTPS tap into an explicit Android intent to open Himo.
webApp.MapGet("/join/group/{id:guid}", (Guid id, HttpRequest request) =>
{
    var invite = request.Query["invite"].ToString().Trim();
    if (id == Guid.Empty || invite.Length is < 16 or > 80)
    {
        return Results.Content(
            "<!doctype html><html lang=\"ar\" dir=\"rtl\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>رابط غير صالح</title><body style=\"font-family:Arial,sans-serif;background:#090d22;color:#fff;text-align:center;padding:48px 20px\"><h2>رابط دعوة المجموعة غير صالح</h2><p>اطلب من مشرف المجموعة إرسال رابط جديد.</p></body></html>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var encodedInvite = Uri.EscapeDataString(invite);
    var appLink = $"himo://group/{id:D}?invite={encodedInvite}";
    var intentLink = $"intent://group/{id:D}?invite={encodedInvite}#Intent;scheme=himo;package=com.companyname.himo;end";
    var safeAppLink = System.Net.WebUtility.HtmlEncode(appLink);
    var safeIntentLink = System.Net.WebUtility.HtmlEncode(intentLink);

    var html = $$"""
<!doctype html>
<html lang="ar" dir="rtl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <meta name="color-scheme" content="dark">
  <meta name="referrer" content="no-referrer">
  <title>دعوة مجموعة Himo</title>
  <style>
    *{box-sizing:border-box}
    body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;background:radial-gradient(circle at 15% 10%,#28145d 0,transparent 38%),#080d22;color:#f7f5ff;font-family:Arial,sans-serif}
    main{width:min(440px,100%);padding:34px 24px;border:1px solid #47317d;border-radius:28px;background:rgba(16,20,52,.94);text-align:center;box-shadow:0 20px 70px #0008}
    .logo{width:66px;height:66px;margin:0 auto 20px;display:grid;place-items:center;border-radius:21px;background:linear-gradient(135deg,#9b55ff,#5734d2);font-size:32px;font-weight:800}
    h1{font-size:24px;margin:0 0 12px}p{color:#c4c8e2;line-height:1.8;margin:0 0 25px}
    a{display:block;text-decoration:none;border-radius:16px;padding:16px 18px;background:linear-gradient(100deg,#9b55ff,#6544e8);color:white;font-weight:700;font-size:17px}
    .direct{margin-top:15px;background:transparent;border:1px solid #62528e;color:#d9ccff;font-size:14px}
    small{display:block;margin-top:22px;color:#969fca;line-height:1.7}
  </style>
</head>
<body>
  <main>
    <div class="logo">H</div>
    <h1>دعوة للانضمام إلى مجموعة</h1>
    <p>اضغط لفتح المجموعة في تطبيق Himo مباشرةً، وسيتم فتح المحادثة بعد التحقق من الدعوة.</p>
    <a href="{{safeIntentLink}}">فتح المجموعة في Himo</a>
    <a class="direct" href="{{safeAppLink}}">فتح الرابط مباشرة</a>
    <small>إذا لم يفتح التطبيق، تأكد من تثبيت Himo على جهازك ثم جرّب مرة أخرى.</small>
  </main>
</body>
</html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
});

// Public-profile share landing page. WhatsApp opens this ordinary HTTPS page;
// the explicit button then invokes the app's already-registered himo://user route.
// The user ID is public routing data only; profile details remain behind the app API.
webApp.MapGet("/join/user/{id:guid}", (Guid id) =>
{
    if (id == Guid.Empty)
    {
        return Results.Content(
            "<!doctype html><html lang=\"ar\" dir=\"rtl\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>رابط غير صالح</title><body style=\"font-family:Arial,sans-serif;background:#090d22;color:#fff;text-align:center;padding:48px 20px\"><h2>رابط الملف الشخصي غير صالح</h2><p>اطلب من صاحب الحساب إرسال رابط جديد.</p></body></html>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var appLink = $"himo://user/{id:D}";
    var intentLink = $"intent://user/{id:D}#Intent;scheme=himo;package=com.companyname.himo;end";
    var safeAppLink = System.Net.WebUtility.HtmlEncode(appLink);
    var safeIntentLink = System.Net.WebUtility.HtmlEncode(intentLink);

    var html = $$"""
<!doctype html>
<html lang="ar" dir="rtl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <meta name="color-scheme" content="dark">
  <meta name="referrer" content="no-referrer">
  <meta property="og:title" content="ملف شخصي على Himo">
  <meta property="og:description" content="افتح الملف الشخصي وتواصل عبر تطبيق Himo.">
  <title>ملف شخصي على Himo</title>
  <style>
    *{box-sizing:border-box}
    body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;background:radial-gradient(circle at 15% 10%,#28145d 0,transparent 38%),#080d22;color:#f7f5ff;font-family:Arial,sans-serif}
    main{width:min(440px,100%);padding:34px 24px;border:1px solid #47317d;border-radius:28px;background:rgba(16,20,52,.94);text-align:center;box-shadow:0 20px 70px #0008}
    .logo{width:66px;height:66px;margin:0 auto 20px;display:grid;place-items:center;border-radius:21px;background:linear-gradient(135deg,#9b55ff,#5734d2);font-size:32px;font-weight:800}
    h1{font-size:24px;margin:0 0 12px}p{color:#c4c8e2;line-height:1.8;margin:0 0 25px}
    a{display:block;text-decoration:none;border-radius:16px;padding:16px 18px;background:linear-gradient(100deg,#9b55ff,#6544e8);color:white;font-weight:700;font-size:17px}
    .direct{margin-top:15px;background:transparent;border:1px solid #62528e;color:#d9ccff;font-size:14px}
    small{display:block;margin-top:22px;color:#969fca;line-height:1.7}
  </style>
</head>
<body>
  <main>
    <div class="logo">H</div>
    <h1>ملف شخصي على Himo</h1>
    <p>تمت مشاركة حساب معك. افتح تطبيق Himo للانتقال مباشرةً إلى المحادثة.</p>
    <a href="{{safeIntentLink}}">فتح المحادثة في Himo</a>
    <a class="direct" href="{{safeAppLink}}">فتح الرابط مباشرة</a>
    <small>إذا لم يفتح التطبيق، تأكد من تثبيت Himo على جهازك ثم جرّب مرة أخرى.</small>
  </main>
</body>
</html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
});

webApp.MapGet("/api/app-info", (IConfiguration configuration) =>
{
    var configuredUrl = configuration["HIMO_APP_DOWNLOAD_URL"]?.Trim();
    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var downloadUri) ||
        downloadUri is null ||
        downloadUri.Scheme != Uri.UriSchemeHttps)
    {
        configuredUrl = null;
    }
    else
    {
        configuredUrl = downloadUri.ToString();
    }

    return Results.Ok(new
    {
        appName = "Himo",
        downloadUrl = configuredUrl,
        available = !string.IsNullOrWhiteSpace(configuredUrl)
    });
});

webApp.MapGet("/health/database", (IServiceProvider services, ILoggerFactory loggerFactory) =>
{
    // Resolve MongoStore only for this request. A missing/invalid MongoDB
    // setting must not prevent the API process and its liveness check from starting.
    try
    {
        var store = services.GetRequiredService<MongoStore>();
        var database = store.GetHealthStatus();
        var healthy = database.Connected && database.SchemaReady;
        return (IResult)Results.Json(new
        {
            status = healthy ? "ok" : "degraded",
            service = "Himo.Api",
            database = database.Connected,
            databaseSchema = database.SchemaReady,
            databaseError = database.Error
        }, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("Himo.DatabaseHealth").LogError(ex,
            "[HimoHealth] MongoDB readiness check failed.");
        return (IResult)Results.Json(new
        {
            status = "degraded",
            service = "Himo.Api",
            database = false,
            databaseSchema = false,
            databaseError = ex.GetType().Name
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
webApp.MapHub<HimoChatHub>("/hubs/chat");

webApp.MapPost("/api/auth/request-email-verification", async (EmailRegisterRequest request, MongoStore store, EmailVerificationService emailService, IHostEnvironment environment, CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var name = request.Name?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "الاسم مطلوب." });
    if (name.Length > 60) return Results.BadRequest(new { message = "الاسم طويل جدًا." });
    var contactPhoneNumber = NormalizeOptionalContactPhone(request.ContactPhoneNumber);
    if (contactPhoneNumber.Error is not null) return Results.BadRequest(new { message = contactPhoneNumber.Error });
    var password = request.Password ?? string.Empty;
    if (!PasswordRules.IsValid(password)) return Results.BadRequest(new { message = "كلمة المرور يجب أن تكون بين 8 و128 حرفًا." });

    // Consent must be explicit and must refer to the exact legal document versions
    // currently published by this API. Never trust client-supplied timestamps.
    if (!request.TermsAccepted || !request.PrivacyAccepted)
        return Results.BadRequest(new { message = "يجب الموافقة على شروط الاستخدام وسياسة الخصوصية قبل إنشاء الحساب." });
    if (!string.Equals(request.TermsVersion?.Trim(), CurrentTermsVersion, StringComparison.Ordinal) ||
        !string.Equals(request.PrivacyVersion?.Trim(), CurrentPrivacyVersion, StringComparison.Ordinal))
        return Results.Json(new { message = "تم تحديث الشروط أو سياسة الخصوصية. حدّث التطبيق ثم وافق على النسخة الحالية." }, statusCode: StatusCodes.Status409Conflict);

    if (store.EmailExists(email)) return Results.Conflict(new { message = "هذا البريد الإلكتروني مستخدم بالفعل." });

    var consentAcceptedAt = DateTimeOffset.UtcNow;
    var code = environment.IsDevelopment()
        ? "123456"
        : RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
    if (!store.TrySetEmailVerification(
            email, name, PasswordRules.Hash(password), contactPhoneNumber.Value, code, expiresAt,
            consentAcceptedAt, CurrentTermsVersion, CurrentPrivacyVersion,
            out var retryAfterSeconds))
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

webApp.MapPost("/api/auth/verify-email", (VerifyEmailRequest request, MongoStore store) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var code = NormalizeDigits(request.Code?.Trim());
    if (code.Length != 6 || code.Any(ch => ch < '0' || ch > '9'))
        return Results.Json(new { message = "رمز التحقق يجب أن يتكون من 6 أرقام." }, statusCode: StatusCodes.Status401Unauthorized);

    if (!store.TryTakeEmailVerification(email, code, out var pending) || pending is null)
        return Results.Json(new { message = "رمز التحقق غير صحيح أو منتهي، أو لم تُسجّل الموافقة القانونية." }, statusCode: StatusCodes.Status401Unauthorized);

    if (!string.Equals(pending.TermsVersion, CurrentTermsVersion, StringComparison.Ordinal) ||
        !string.Equals(pending.PrivacyVersion, CurrentPrivacyVersion, StringComparison.Ordinal))
        return Results.Json(new { message = "تغيرت الشروط أثناء التحقق. أعد طلب رمز التحقق بعد الموافقة على النسخة الحالية." }, statusCode: StatusCodes.Status409Conflict);

    try
    {
        var user = store.CreateEmailUser(
            email, pending.Name, pending.PasswordHash, true, pending.ContactPhoneNumber,
            pending.TermsAcceptedAt, pending.TermsVersion,
            pending.PrivacyAcceptedAt, pending.PrivacyVersion);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        store.CreateSession(token, user.Id, DateTimeOffset.UtcNow.AddDays(30), request.DeviceId, request.DeviceName, request.DeviceType);
        return Results.Ok(new AuthResponse(token, user.Id, email, user.Name));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
}).RequireRateLimiting("auth");

webApp.MapPost("/api/auth/login-email", (EmailLoginRequest request, MongoStore store) =>
{
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    var password = request.Password ?? string.Empty;
    if (!PasswordRules.IsValid(password)) return Results.Unauthorized();
    var user = store.AuthenticateEmail(email, password);
    if (user is null) return Results.Json(new { message = "البريد الإلكتروني أو كلمة المرور غير صحيحة." }, statusCode: StatusCodes.Status401Unauthorized);
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    store.CreateSession(token, user.Id, DateTimeOffset.UtcNow.AddDays(30), request.DeviceId, request.DeviceName, request.DeviceType);
    return Results.Ok(new AuthResponse(token, user.Id, email, user.Name));
}).RequireRateLimiting("auth");

webApp.MapPost("/api/auth/request-password-reset", async (PasswordResetRequest request, MongoStore store, EmailVerificationService emailService, IHostEnvironment environment, CancellationToken cancellationToken) =>
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

webApp.MapPost("/api/auth/reset-password", (PasswordResetConfirmRequest request, MongoStore store) =>
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
    store.CreateSession(token, result!.UserId, DateTimeOffset.UtcNow.AddDays(30), request.DeviceId, request.DeviceName, request.DeviceType);
    return Results.Ok(new AuthResponse(token, result.UserId, result.Email, result.Name));
}).RequireRateLimiting("auth");

webApp.MapPost("/api/auth/logout", (HttpRequest http, MongoStore store) =>
    store.RevokeSession(http) ? Results.NoContent() : Results.Unauthorized());

webApp.MapPost("/api/auth/logout-all", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    store.RevokeAllSessions(session.UserId);
    return Results.NoContent();
});

// Browser pairing is a short-lived two-secret handshake. The QR secret is
// scanned by the authenticated phone; the independent poll secret stays only
// in the browser tab. A QR image alone never contains an account session token.
webApp.MapPost("/api/web/pair/start", (StartWebPairingRequest request, MongoStore store, HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    if (!IsSha256Hex(request.QrKeyHash) || !IsSha256Hex(request.PollSecretHash))
        return Results.BadRequest(new { message = "بيانات طلب ربط المتصفح غير صالحة." });
    var browserName = string.IsNullOrWhiteSpace(request.BrowserName) ? "متصفح الكمبيوتر" : request.BrowserName.Trim();
    if (browserName.Length > 80) browserName = browserName[..80];
    var id = store.CreateWebPairing(request.QrKeyHash!, request.PollSecretHash!, browserName, DateTimeOffset.UtcNow.AddMinutes(2));
    return Results.Ok(new { pairingId = id, expiresInSeconds = 120 });
}).RequireRateLimiting("web-pair-start");

// Generate QR PNG on the same server so the browser does not execute third-party
// JavaScript in the origin that holds its Himo session token.
webApp.MapPost("/api/web/pair/qr", (WebPairingQrRequest request, HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
    if (string.IsNullOrWhiteSpace(request.Payload) ||
        request.Payload.Length > 512 ||
        !request.Payload.StartsWith("himo://link-device/", StringComparison.Ordinal) ||
        request.Payload.Any(char.IsControl))
        return Results.BadRequest(new { message = "محتوى رمز QR غير صالح." });

    using var qrData = QRCodeGenerator.GenerateQrCode(request.Payload, QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(qrData).GetGraphic(8);
    return Results.File(png, "image/png");
}).RequireRateLimiting("web-pair-start");

webApp.MapPost("/api/web/pair/poll", (PollWebPairingRequest request, MongoStore store, HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
    if (request.PairingId == Guid.Empty || !IsSecretHex(request.PollSecret))
        return Results.BadRequest(new { message = "طلب الربط غير صالح." });
    var result = store.PollWebPairing(request.PairingId, request.PollSecret!);
    if (result is null) return Results.Unauthorized();
    return Results.Ok(new { status = result.Status, accessToken = result.AccessToken, deviceName = result.DeviceName });
}).RequireRateLimiting("web-pair-poll");

webApp.MapPost("/api/web/pair/complete", (PollWebPairingRequest request, MongoStore store, HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
    if (request.PairingId == Guid.Empty || !IsSecretHex(request.PollSecret))
        return Results.BadRequest(new { message = "طلب الربط غير صالح." });
    return store.CompleteWebPairing(request.PairingId, request.PollSecret!)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireRateLimiting("web-pair-poll");

webApp.MapPost("/api/devices/pair/approve", (HttpRequest http, ApproveWebPairingRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (request.PairingId == Guid.Empty || !IsSecretHex(request.QrKey))
        return Results.BadRequest(new { message = "رمز ربط الكمبيوتر غير صالح." });
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    if (!store.ApproveWebPairing(request.PairingId, request.QrKey!, session.UserId, token, DateTimeOffset.UtcNow.AddDays(30)))
        return Results.Conflict(new { message = "انتهت صلاحية رمز الربط أو تم استخدامه مسبقًا. حدّث صفحة الكمبيوتر وامسح الرمز الجديد." });
    return Results.Ok(new { linked = true, deviceName = request.DeviceName });
}).RequireRateLimiting("device-pair-approve");

// A phone-to-phone pairing is initiated by an authenticated primary phone.
// The QR carries only a one-time claim secret. The companion generates its own
// polling secret; the primary phone must explicitly approve the named device.
webApp.MapPost("/api/devices/mobile-pair/start", (HttpRequest http, StartMobilePairingRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!IsSha256Hex(request.QrKeyHash)) return Results.BadRequest(new { message = "بيانات ربط الهاتف غير صالحة." });
    var id = store.CreateMobilePairing(session.UserId, request.QrKeyHash!, DateTimeOffset.UtcNow.AddMinutes(2));
    return Results.Ok(new { pairingId = id, expiresInSeconds = 120 });
}).RequireRateLimiting("device-pair-approve");

webApp.MapPost("/api/devices/mobile-pair/claim", (MobilePairingClaimRequest request, MongoStore store) =>
{
    if (request.PairingId == Guid.Empty || !IsSecretHex(request.QrKey) || !IsSha256Hex(request.PollSecretHash)) return Results.BadRequest(new { message = "رمز ربط الهاتف غير صالح." });
    var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "هاتف Himo مساعد" : request.DeviceName.Trim();
    if (deviceName.Length > 80) deviceName = deviceName[..80];
    var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "mobile-" + Guid.NewGuid().ToString("N") : request.DeviceId.Trim();
    if (deviceId.Length > 100) return Results.BadRequest(new { message = "معرّف الهاتف غير صالح." });
    return store.ClaimMobilePairing(request.PairingId, request.QrKey!, request.PollSecretHash!, deviceId, deviceName)
        ? Results.Ok(new { claimed = true })
        : Results.Conflict(new { message = "انتهت صلاحية رمز الربط أو سبق استخدامه." });
}).RequireRateLimiting("web-pair-poll");

webApp.MapGet("/api/devices/mobile-pair/pending", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    return Results.Ok(store.GetPendingMobilePairings(session.UserId));
}).RequireRateLimiting("web-pair-poll");

webApp.MapPost("/api/devices/mobile-pair/approve", (HttpRequest http, MobilePairingDecisionRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (request.PairingId == Guid.Empty) return Results.BadRequest();
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    return store.ApproveMobilePairing(session.UserId, request.PairingId, token, DateTimeOffset.UtcNow.AddDays(30))
        ? Results.Ok(new { approved = true })
        : Results.Conflict(new { message = "طلب الربط لم يعد صالحًا." });
}).RequireRateLimiting("device-pair-approve");

webApp.MapPost("/api/devices/mobile-pair/poll", (MobilePairingPollRequest request, MongoStore store) =>
{
    if (request.PairingId == Guid.Empty || !IsSecretHex(request.PollSecret)) return Results.BadRequest();
    var result = store.PollMobilePairing(request.PairingId, request.PollSecret!);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
}).RequireRateLimiting("web-pair-poll");

webApp.MapPost("/api/devices/mobile-pair/complete", (MobilePairingPollRequest request, MongoStore store) =>
{
    if (request.PairingId == Guid.Empty || !IsSecretHex(request.PollSecret)) return Results.BadRequest();
    return store.CompleteMobilePairing(request.PairingId, request.PollSecret!) ? Results.NoContent() : Results.NotFound();
}).RequireRateLimiting("web-pair-poll");

webApp.MapPost("/api/devices/mobile-pair/cancel", (HttpRequest http, MobilePairingDecisionRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    return store.CancelMobilePairing(session.UserId, request.PairingId) ? Results.NoContent() : Results.NotFound();
}).RequireRateLimiting("device-pair-approve");

webApp.MapGet("/api/me/devices", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var devices = store.GetLinkedDevices(session.UserId).Select(device => new
    {
        device.DeviceId,
        device.DeviceName,
        device.DeviceType,
        device.CreatedAt,
        device.LastSeenAt,
        isCurrent = string.Equals(device.DeviceId, session.DeviceId, StringComparison.Ordinal)
    });
    return Results.Ok(devices);
});

webApp.MapDelete("/api/me/devices/{deviceId}", (string deviceId, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 100) return Results.BadRequest();
    return store.RevokeLinkedDevice(session.UserId, deviceId) ? Results.NoContent() : Results.NotFound();
});

webApp.MapGet("/api/me", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    return Results.Ok(store.GetProfile(session.UserId));
});

// Profile links reveal only public fields and require an authenticated Himo session.
webApp.MapGet("/api/users/{id:guid}/profile", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try { return Results.Ok(store.GetPublicProfile(id)); }
    catch (InvalidOperationException) { return Results.NotFound(); }
});

webApp.MapDelete("/api/me", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        store.DeleteAccount(session.UserId);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapPut("/api/me", (HttpRequest http, UpdateProfileRequest request, MongoStore store) =>
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

webApp.MapPut("/api/me/username", (HttpRequest http, UpdateUsernameRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();
    var validChars = username.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '.');
    if (username.Length is < 3 or > 24 || !validChars || username.StartsWith('.') || username.EndsWith('.') || username.Contains("..", StringComparison.Ordinal))
        return Results.BadRequest(new { message = "اسم المستخدم يجب أن يكون بين 3 و24 حرفًا، ويحتوي على أحرف إنجليزية أو أرقام أو (_) أو (.) فقط." });
    if (!store.TryUpdateUsername(session.UserId, username, out var error))
        return Results.Conflict(new { message = error ?? "اسم المستخدم مستخدم بالفعل." });
    return Results.Ok(new { username });
});

webApp.MapPost("/api/me/request-email-change", async (HttpRequest http, EmailChangeRequest request, MongoStore store, EmailVerificationService emailService, IHostEnvironment environment, CancellationToken cancellationToken) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var email = NormalizeEmail(request.Email);
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "أدخل بريدًا إلكترونيًا صحيحًا." });
    var current = store.GetAccountEmailAndName(session.UserId);
    if (string.Equals(current.Email, email, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "هذا هو البريد الإلكتروني الحالي للحساب." });
    if (store.EmailExists(email))
        return Results.Conflict(new { message = "هذا البريد الإلكتروني مستخدم بالفعل في حساب آخر." });

    var code = environment.IsDevelopment()
        ? "123456"
        : RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
    if (!store.TrySetEmailChangeVerification(session.UserId, email, code, DateTimeOffset.UtcNow.AddMinutes(10), out var retryAfterSeconds))
        return Results.Json(new { message = $"انتظر {retryAfterSeconds} ثانية قبل طلب رمز جديد." }, statusCode: StatusCodes.Status429TooManyRequests);

    if (environment.IsDevelopment())
        return Results.Ok(new { message = "تم إنشاء رمز التأكيد.", developmentCode = code });

    try
    {
        await emailService.SendEmailChangeCodeAsync(email, code, cancellationToken);
        return Results.Ok(new { message = "أرسلنا رمز التأكيد إلى البريد الجديد." });
    }
    catch (EmailProviderNotConfiguredException ex)
    {
        store.RemoveEmailChangeVerification(session.UserId);
        return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (EmailProviderException ex)
    {
        store.RemoveEmailChangeVerification(session.UserId);
        Console.Error.WriteLine($"[Himo.EmailChange] Brevo HTTP {ex.StatusCode}: {ex.ProviderResponse}");
        return Results.Json(new { message = "تعذر إرسال رمز التأكيد إلى البريد الجديد. حاول لاحقًا." }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        store.RemoveEmailChangeVerification(session.UserId);
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch
    {
        store.RemoveEmailChangeVerification(session.UserId);
        return Results.Json(new { message = "تعذر إرسال رمز التأكيد. حاول لاحقًا." }, statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("otp-request");

webApp.MapPost("/api/me/verify-email-change", (HttpRequest http, VerifyEmailChangeRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var email = NormalizeEmail(request.Email);
    var code = NormalizeDigits(request.Code?.Trim());
    if (!IsValidEmail(email)) return Results.BadRequest(new { message = "البريد الإلكتروني غير صحيح." });
    if (code.Length != 6 || code.Any(ch => ch < '0' || ch > '9'))
        return Results.Json(new { message = "أدخل رمز التأكيد المكوّن من 6 أرقام." }, statusCode: StatusCodes.Status401Unauthorized);
    if (!store.TryConfirmEmailChange(session.UserId, email, code, out var error))
    {
        var conflict = error?.Contains("مستخدم بالفعل", StringComparison.Ordinal) == true;
        return Results.Json(new { message = error ?? "رمز التأكيد غير صحيح أو منتهي." }, statusCode: conflict ? StatusCodes.Status409Conflict : StatusCodes.Status401Unauthorized);
    }
    return Results.Ok(store.GetProfile(session.UserId));
});

webApp.MapGet("/api/me/photo", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var photo = store.GetProfilePhoto(session.UserId);
    var photoBytes = photo.Data;
    if (photoBytes is null || photoBytes.Length == 0) return Results.NotFound();
    return Results.File(photoBytes, photo.ContentType);
});

// Profile photos for other participants are visible only to authenticated Himo users.
webApp.MapGet("/api/users/{id:guid}/photo", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        var photo = store.GetProfilePhoto(id);
        var photoBytes = photo.Data;
        if (photoBytes is null || photoBytes.Length == 0) return Results.NotFound();
        return Results.File(photoBytes, photo.ContentType);
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
});

webApp.MapPut("/api/me/photo", (HttpRequest http, ProfilePhotoUploadRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();

    var contentType = (request.ContentType ?? string.Empty).Trim().ToLowerInvariant();
    if (contentType == "image/jpg") contentType = "image/jpeg";
    if (!IsSupportedProfilePhotoType(contentType))
        return Results.BadRequest(new { message = "صيغة الصورة غير مدعومة. استخدم JPG أو PNG أو WebP أو HEIC." });

    if (string.IsNullOrWhiteSpace(request.Base64Data) || request.Base64Data.Length > 6 * 1024 * 1024)
        return Results.BadRequest(new { message = "حجم الصورة غير صالح أو أكبر من 4 ميغابايت." });

    byte[] bytes;
    try { bytes = Convert.FromBase64String(request.Base64Data); }
    catch (FormatException) { return Results.BadRequest(new { message = "بيانات الصورة غير صالحة." }); }

    if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024 || !IsValidProfilePhotoPayload(bytes, contentType))
        return Results.BadRequest(new { message = "الملف المحدد ليس صورة مدعومة أو يتجاوز 4 ميغابايت." });

    store.UpdateProfilePhoto(session.UserId, bytes, contentType);
    return Results.Ok(new { hasProfilePhoto = true });
});

webApp.MapDelete("/api/me/photo", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    store.DeleteProfilePhoto(session.UserId);
    return Results.NoContent();
});

webApp.MapGet("/api/users/group-search", (HttpRequest http, string? query, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();

    var value = (query ?? string.Empty).Trim();
    if (value.Length < 3 || value.Length > 254)
        return Results.BadRequest(new { message = "أدخل ثلاثة أحرف على الأقل للبحث بالاسم، أو بريدًا إلكترونيًا أو رقم هاتف صحيحًا." });

    // Email searches are exact to avoid returning accounts whose address merely
    // starts with the supplied text. This app stores verified email in Users.PhoneNumber.
    if (value.Contains('@'))
    {
        var email = NormalizeEmail(value);
        if (!IsValidEmail(email))
            return Results.BadRequest(new { message = "صيغة البريد الإلكتروني غير صحيحة." });
        return Results.Ok(store.SearchGroupMembers(email, null, session.UserId));
    }

    // Only strings consisting of phone-like characters are treated as phone
    // queries. Anything else is searched against the display name.
    if (IsPhoneLookupQuery(value))
    {
        var normalizedPhone = NormalizeContactPhoneForMatching(value);
        if (normalizedPhone.Error is not null || normalizedPhone.Value is null)
            return Results.BadRequest(new { message = normalizedPhone.Error ?? "أدخل رقم هاتف صحيحًا." });
        return Results.Ok(store.SearchGroupMembers(null, normalizedPhone.Value, session.UserId));
    }

    return Results.Ok(store.SearchUsersByName(value, session.UserId));
});

webApp.MapGet("/api/users/search", (HttpRequest http, string? email, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var normalized = NormalizeEmail(email);
    if (!IsValidEmail(normalized)) return Results.BadRequest(new { message = "أدخل بريدًا إلكترونيًا صحيحًا للبحث." });
    var users = store.SearchUsers(normalized, session.UserId);
    return Results.Ok(users);
});

webApp.MapPut("/api/me/contact-phone", (HttpRequest http, UpdateContactPhoneRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();

    var normalized = NormalizeContactPhoneForMatching(request.ContactPhoneNumber);
    if (normalized.Error is not null)
        return Results.BadRequest(new { message = normalized.Error });
    if (request.PhoneDiscoverable && normalized.Value is null)
        return Results.BadRequest(new { message = "أدخل رقم الهاتف قبل السماح للآخرين بالعثور على حسابك." });

    var phoneDiscoverable = request.PhoneDiscoverable && normalized.Value is not null;
    if (!store.TryUpdateContactPhone(session.UserId, normalized.Value, phoneDiscoverable, out var updateError))
        return Results.Conflict(new { message = updateError ?? "تعذر تحديث رقم الهاتف." });

    return Results.Ok(new
    {
        contactPhoneNumber = normalized.Value is null ? null : "+" + normalized.Value,
        phoneDiscoverable
    });
});

// Phone numbers are used for matching only and are not persisted by this endpoint.
// The response contains the account id/name and only the phone number matched from the caller-supplied list, so the app can associate a result with its local contact. The address book is not stored.
webApp.MapPost("/api/contacts/lookup", (HttpRequest http, ContactLookupRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var submitted = request.PhoneNumbers ?? Array.Empty<string>();
    if (submitted.Length > 250)
        return Results.BadRequest(new { message = "أرسل 250 رقمًا كحد أقصى في كل طلب." });

    var normalizedNumbers = new HashSet<string>(StringComparer.Ordinal);
    foreach (var phone in submitted)
    {
        var normalized = NormalizeContactPhoneForMatching(phone);
        if (normalized.Value is not null)
            normalizedNumbers.Add(normalized.Value);
    }
    if (normalizedNumbers.Count == 0)
        return Results.Ok(Array.Empty<ContactMatchResponse>());

    return Results.Ok(store.FindUsersByPhoneNumbers(normalizedNumbers, session.UserId));
});

webApp.MapPost("/api/push-token", (HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] PushTokenRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var token = request.Token?.Trim() ?? string.Empty;
    if (token.Length < 20 || token.Length > 4096)
        return Results.BadRequest(new { message = "رمز إشعارات الجهاز غير صحيح." });

    store.SavePushToken(session.UserId, token, session.DeviceId);
    return Results.NoContent();
});

webApp.MapDelete("/api/push-token", (HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] PushTokenRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var token = request.Token?.Trim() ?? string.Empty;
    if (token.Length == 0) return Results.NoContent();
    store.RemovePushToken(session.UserId, token);
    return Results.NoContent();
});

webApp.MapGet("/api/conversations", (HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    return Results.Ok(store.GetConversations(session.UserId));
});

webApp.MapGet("/api/conversations/{id:guid}/disappearing", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    return Results.Ok(new { seconds = store.GetDisappearingSeconds(id) });
});

webApp.MapPost("/api/conversations/{id:guid}/disappearing", (Guid id, HttpRequest http, SetDisappearingSecondsRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    if (request.Seconds is not (0 or 3600 or 7200 or 86400 or 432000 or 2592000))
        return Results.BadRequest(new { message = "قيمة مؤقت الاختفاء غير مدعومة." });
    store.SetDisappearingSeconds(id, session.UserId, request.Seconds);
    return Results.Ok(new { seconds = request.Seconds });
});

webApp.MapPost("/api/conversations/{id:guid}/clear", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    store.ClearConversationForUser(id, session.UserId);
    return Results.Ok(new { cleared = true });
});

webApp.MapPost("/api/translate", async (HttpRequest http, TranslateRequest request, IHttpClientFactory clients, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var text = request.Text?.Trim() ?? string.Empty;
    if (text.Length == 0) return Results.BadRequest(new { message = "النص مطلوب للترجمة." });
    if (text.Length > 4000) return Results.BadRequest(new { message = "النص طويل جدًا للترجمة." });
    var target = (request.TargetLanguage ?? "ar").Trim().ToLowerInvariant();
    if (!System.Text.RegularExpressions.Regex.IsMatch(target, "^[a-z]{2,3}(-[a-z]{2})?$") || target.Length > 5)
        return Results.BadRequest(new { message = "لغة الترجمة غير صالحة." });
    var configuredUrl = Environment.GetEnvironmentVariable("LIBRETRANSLATE_URL") ?? Environment.GetEnvironmentVariable("TRANSLATION_API_URL");
    if (string.IsNullOrWhiteSpace(configuredUrl) || !Uri.TryCreate(configuredUrl, UriKind.Absolute, out _))
        return Results.Json(new { message = "لم تُضبط خدمة الترجمة على الخادم." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    var endpoint = configuredUrl.TrimEnd('/');
    if (!endpoint.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)) endpoint += "/translate";
    var payload = new Dictionary<string, object?> { ["q"] = text, ["source"] = "auto", ["target"] = target, ["format"] = "text" };
    var apiKey = Environment.GetEnvironmentVariable("LIBRETRANSLATE_API_KEY") ?? Environment.GetEnvironmentVariable("TRANSLATION_API_KEY");
    if (!string.IsNullOrWhiteSpace(apiKey)) payload["api_key"] = apiKey;
    try
    {
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var response = await client.PostAsJsonAsync(endpoint, payload, http.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
            return Results.Json(new { message = "فشلت خدمة الترجمة الخارجية." }, statusCode: StatusCodes.Status502BadGateway);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(http.HttpContext.RequestAborted));
        string? translated = null;
        if (document.RootElement.TryGetProperty("translatedText", out var textNode)) translated = textNode.GetString();
        if (string.IsNullOrWhiteSpace(translated) && document.RootElement.TryGetProperty("data", out var dataNode)
            && dataNode.TryGetProperty("translations", out var translationsNode) && translationsNode.ValueKind == JsonValueKind.Array
            && translationsNode.GetArrayLength() > 0 && translationsNode[0].TryGetProperty("translatedText", out var nestedText))
            translated = nestedText.GetString();
        if (string.IsNullOrWhiteSpace(translated))
            return Results.Json(new { message = "استجابة الترجمة غير صالحة." }, statusCode: StatusCodes.Status502BadGateway);
        return Results.Ok(new { translatedText = translated });
    }
    catch (OperationCanceledException) when (http.HttpContext.RequestAborted.IsCancellationRequested) { return Results.StatusCode(499); }
    catch { return Results.Json(new { message = "تعذر الاتصال بخدمة الترجمة." }, statusCode: StatusCodes.Status502BadGateway); }
});

webApp.MapPost("/api/conversations/{id:guid}/actions", (Guid id, HttpRequest http, ConversationActionRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;
    try
    {
        store.ApplyConversationAction(id, session.UserId, action);
        if (action == "delete" && !store.IsGroupConversation(id))
            HimoChatHub.ForgetGroupCallRoom(id);
        return Results.Ok(new { conversationId = id, action });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapPost("/api/conversations/{id:guid}/read", async (Guid id, HttpRequest http, MongoStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    var readReceipts = store.MarkConversationRead(id, session.UserId);
    foreach (var receipt in readReceipts)
        await hub.Clients.Group(HimoChatHub.UserGroup(receipt.SenderUserId))
            .SendAsync("MessageDeliveryChanged", receipt.MessageId, session.UserId, "read");
    return Results.NoContent();
});

webApp.MapPost("/api/messages/{id:guid}/delivered", async (Guid id, HttpRequest http, MongoStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();

    var receipt = store.MarkMessageDelivered(id, session.UserId);
    if (receipt.Changed && receipt.SenderUserId.HasValue)
    {
        await hub.Clients.Group(HimoChatHub.UserGroup(receipt.SenderUserId.Value))
            .SendAsync("MessageDeliveryChanged", id, session.UserId, "delivered");
    }

    return Results.NoContent();
});

webApp.MapPost("/api/conversations/{id:guid}/report", async (Guid id, HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] ReportConversationRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    var reason = (request.Reason ?? string.Empty).Trim();
    if (reason.Length is < 3 or > 200) return Results.BadRequest(new { message = "سبب البلاغ غير صالح." });
    store.ReportConversation(id, session.UserId, reason);
    return Results.Ok(new { reported = true });
});

webApp.MapPost("/api/conversations", (HttpRequest http, CreateConversationRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var name = request.Name?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "اسم المحادثة مطلوب." });
    if (name.Length > 60) return Results.BadRequest(new { message = "اسم المحادثة طويل جدًا." });
    try
    {
        // CreateConversation now returns the participant's canonical name/photo
        // directly. Avoid loading every conversation merely to enrich this one row.
        var conversation = store.CreateConversation(session.UserId, name, request.UserId);
        return Results.Created($"/api/conversations/{conversation.Id}", conversation);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapPost("/api/groups", (HttpRequest http, CreateGroupRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();

    var name = request.Name?.Trim() ?? string.Empty;
    if (name.Length is < 2 or > 60)
        return Results.BadRequest(new { message = "اسم المجموعة يجب أن يتراوح بين حرفين و60 حرفًا." });

    var memberIds = (request.UserIds ?? Array.Empty<Guid>())
        .Where(id => id != Guid.Empty && id != session.UserId)
        .Distinct()
        .ToArray();

    if (memberIds.Length == 0)
        return Results.BadRequest(new { message = "أضف عضوًا واحدًا على الأقل قبل إنشاء المجموعة." });
    if (memberIds.Length > 100)
        return Results.BadRequest(new { message = "يمكن إضافة 100 عضو كحد أقصى إلى المجموعة." });

    if (request.DisappearingSeconds is not (0 or 3600 or 7200 or 86400 or 432000 or 2592000))
        return Results.BadRequest(new { message = "قيمة مؤقت الاختفاء غير مدعومة." });

    try
    {
        var group = store.CreateGroup(
            session.UserId, name, memberIds, request.DisappearingSeconds,
            request.AllMembersCanAddMembers, request.AllMembersCanShareInviteLink,
            request.RequireAdminApproval, request.AdminsOnlyMessages);
        return Results.Created($"/api/conversations/{group.Id}", group);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapPost("/api/groups/{id:guid}/members", (Guid id, HttpRequest http, AddGroupMembersRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var memberIds = (request.UserIds ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty && x != session.UserId).Distinct().ToArray();
    try
    {
        var added = store.AddGroupMembers(id, session.UserId, memberIds);
        return Results.Ok(new { conversationId = id, addedCount = added });
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapGet("/api/groups/{id:guid}", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var details = store.GetGroupDetails(id, session.UserId);
    return details is null ? Results.NotFound() : Results.Ok(details);
});

webApp.MapPut("/api/groups/{id:guid}", (Guid id, HttpRequest http, UpdateGroupInfoRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        if (!store.UpdateGroupInfo(id, session.UserId, request.Name ?? string.Empty, request.Description ?? string.Empty))
            return Results.NotFound();
        return Results.Ok(store.GetGroupDetails(id, session.UserId));
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message);
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
});

webApp.MapGet("/api/groups/{id:guid}/photo", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var photo = store.GetGroupPhoto(id, session.UserId);
    if (photo is null) return Results.NotFound();
    if (photo.Value.Data is not { Length: > 0 } bytes) return Results.NotFound();
    return Results.File(bytes, photo.Value.ContentType);
});

webApp.MapPut("/api/groups/{id:guid}/photo", (Guid id, HttpRequest http, GroupPhotoUploadRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var contentType = (request.ContentType ?? string.Empty).Trim().ToLowerInvariant();
    if (contentType == "image/jpg") contentType = "image/jpeg";
    if (!IsSupportedProfilePhotoType(contentType)) return Results.BadRequest(new { message = "صيغة الصورة غير مدعومة." });
    if (string.IsNullOrWhiteSpace(request.Base64Data) || request.Base64Data.Length > 6 * 1024 * 1024)
        return Results.BadRequest(new { message = "حجم الصورة غير صالح أو أكبر من 4 ميغابايت." });
    byte[] bytes;
    try { bytes = Convert.FromBase64String(request.Base64Data); }
    catch (FormatException) { return Results.BadRequest(new { message = "بيانات الصورة غير صالحة." }); }
    if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024 || !IsValidProfilePhotoPayload(bytes, contentType))
        return Results.BadRequest(new { message = "الملف ليس صورة مدعومة أو يتجاوز 4 ميغابايت." });
    try
    {
        store.UpdateGroupPhoto(id, session.UserId, bytes, contentType);
        return Results.Ok(new { hasPhoto = true });
    }
    catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
});

webApp.MapDelete("/api/groups/{id:guid}/photo", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try { store.DeleteGroupPhoto(id, session.UserId); return Results.NoContent(); }
    catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
});

webApp.MapPut("/api/groups/{id:guid}/members/{memberId:guid}/admin", (Guid id, Guid memberId, HttpRequest http, SetGroupMemberAdminRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        if (!store.PromoteGroupMember(id, session.UserId, memberId, request.IsAdmin)) return Results.NotFound();
        return Results.Ok(new { memberId, isAdmin = request.IsAdmin });
    }
    catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
});

webApp.MapDelete("/api/groups/{id:guid}/members/{memberId:guid}", (Guid id, Guid memberId, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        if (!store.RemoveGroupMember(id, session.UserId, memberId)) return Results.NotFound();
        return Results.NoContent();
    }
    catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
});

webApp.MapDelete("/api/groups/{id:guid}/leave", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        // Minimal API binds this parameter as HttpRequest, so query values are
        // available directly through http.Query (HttpRequest has no Request property).
        var clearHistory = bool.TryParse(http.Query["clearHistory"].ToString(), out var requestedClearHistory)
            && requestedClearHistory;
        if (!store.LeaveGroup(id, session.UserId, clearHistory)) return Results.NotFound();
        if (!store.IsGroupConversation(id))
            HimoChatHub.ForgetGroupCallRoom(id);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

webApp.MapGet("/api/groups/{id:guid}/invite", (Guid id, HttpRequest http, MongoStore store, IConfiguration configuration) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        var invite = store.GetGroupInviteCode(id, session.UserId);
        if (invite is null) return Results.NotFound();

        // Configure HIMO_PUBLIC_BASE_URL only when the public API hostname changes.
        // This defaults to the production API address currently used by Himo clients.
        var publicBaseUrl = (configuration["HIMO_PUBLIC_BASE_URL"] ?? "https://himo-api.onrender.com").Trim().TrimEnd('/');
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
            publicBaseUri is null ||
            !string.Equals(publicBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "تعذر إنشاء رابط آمن للمجموعة.");

        var link = $"{publicBaseUri.GetLeftPart(UriPartial.Authority)}/join/group/{id:D}?invite={Uri.EscapeDataString(invite)}";
        return Results.Ok(new { inviteCode = invite, link });
    }
    catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "غير مسموح", detail: ex.Message); }
});

// Read-only authenticated invite preview. It validates the invite/group pair but
// deliberately does not add the caller to the participant collection.
webApp.MapGet("/api/groups/invite-preview/{id:guid}", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var inviteCode = http.Query["invite"].ToString().Trim();
    if (inviteCode.Length is < 16 or > 80)
        return Results.BadRequest(new { message = "رمز دعوة المجموعة غير صالح." });

    var preview = store.GetGroupInvitePreview(inviteCode, session.UserId, id);
    return preview is null ? Results.NotFound() : Results.Ok(preview);
});

webApp.MapPost("/api/groups/join", (HttpRequest http, JoinGroupByInviteRequest request, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    try
    {
        var conversation = store.JoinGroupByInvite(
            request.InviteCode ?? string.Empty,
            session.UserId,
            request.ExpectedGroupId);
        return conversation is null ? Results.NotFound() : Results.Ok(conversation);
    }
    catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "يتطلب موافقة المشرف", detail: ex.Message); }
});

webApp.MapGet("/api/conversations/{id:guid}/messages", (Guid id, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();

    DateTimeOffset? since = null;
    DateTimeOffset? before = null;
    Guid? beforeId = null;
    var limit = 30;

    if (http.Query.TryGetValue("since", out var sinceValue) && !string.IsNullOrWhiteSpace(sinceValue))
    {
        if (!DateTimeOffset.TryParse(sinceValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedSince))
            return Results.BadRequest(new { message = "وقت المزامنة غير صحيح." });
        since = parsedSince;
    }

    if (http.Query.TryGetValue("before", out var beforeValue) && !string.IsNullOrWhiteSpace(beforeValue))
    {
        if (!DateTimeOffset.TryParse(beforeValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedBefore))
            return Results.BadRequest(new { message = "وقت الرسائل الأقدم غير صحيح." });
        before = parsedBefore;
    }

    if (http.Query.TryGetValue("beforeId", out var beforeIdValue) && !string.IsNullOrWhiteSpace(beforeIdValue))
    {
        if (!Guid.TryParse(beforeIdValue.ToString(), out var parsedBeforeId))
            return Results.BadRequest(new { message = "معرّف مؤشر الرسائل الأقدم غير صحيح." });
        beforeId = parsedBeforeId;
    }

    if (http.Query.TryGetValue("limit", out var limitValue) && int.TryParse(limitValue.ToString(), out var requestedLimit))
        limit = Math.Clamp(requestedLimit, 1, 1000);

    return Results.Ok(store.GetMessages(id, since, before, session.UserId, beforeId, limit));
});

webApp.MapPost("/api/conversations/{id:guid}/messages", async (Guid id, HttpRequest http, [Microsoft.AspNetCore.Mvc.FromBody] SendMessageRequest request, MongoStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var sendContext = store.GetMessageSendContext(id, session.UserId);
    if (!sendContext.Exists) return Results.NotFound();
    if (sendContext.IsBlocked)
        return Results.Conflict(new { message = "لا يمكن إرسال رسائل في هذه المحادثة لأن أحد الطرفين حظر الآخر." });
    if (!store.CanSendGroupMessage(id, session.UserId))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "الإرسال مقيد", detail: "يسمح المشرفون فقط بإرسال الرسائل في هذه المجموعة.");
    var recipientIds = sendContext.RecipientUserIds;
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
    if (request.ReplyToMessageId.HasValue && !store.MessageBelongsToConversation(request.ReplyToMessageId.Value, id))
        return Results.BadRequest(new { message = "الرسالة المقتبس منها غير موجودة في هذه المحادثة." });
    var message = store.AddMessage(id, session.UserId, session.PhoneNumber, text, clientMessageId, request.ReplyToMessageId);

    // The message is already persisted. Return 201 immediately so the sender
    // can show its checkmark without waiting for realtime delivery or FCM.
    // OnCompleted starts both delivery paths only after ASP.NET Core has
    // completed the HTTP response, preventing a slow FCM request from delaying
    // the sender's acknowledgement.
    http.HttpContext.Response.OnCompleted(async () =>
    {
        try
        {
            await hub.Clients.Groups(recipientIds.Select(userId => HimoChatHub.UserGroup(userId)))
                .SendAsync("MessageReceived", message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Himo.SignalR] Message delivery failed for {message.Id}: {ex}");
        }

        try
        {
            var tokens = store.GetPushTokens(store.GetUnmutedNotificationRecipients(id, recipientIds));
            var push = webApp.Services.GetRequiredService<FcmPushService>();
            var acceptedTokens = await push.SendMessageAsync(
                tokens,
                session.Name,
                message.Text,
                message.ConversationId,
                message.Id,
                CancellationToken.None,
                isGroup: store.IsGroupConversation(message.ConversationId));

            // FCM accepted the message for these registered device tokens.
            // Treat that as the delivered state for the message receipt so the
            // sender can see the two grey checks even when the recipient app is
            // fully closed. Opening the conversation still changes the state
            // to blue via the existing read-receipt path.
            var deliveredUserIds = store.GetPushTokenOwners(acceptedTokens);
            foreach (var deliveredUserId in deliveredUserIds)
            {
                var receipt = store.MarkMessageDelivered(message.Id, deliveredUserId);
                if (receipt.Changed && receipt.SenderUserId.HasValue)
                {
                    await hub.Clients.Group(HimoChatHub.UserGroup(receipt.SenderUserId.Value))
                        .SendAsync("MessageDeliveryChanged", message.Id, deliveredUserId, "delivered");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Himo.FCM] Message notification failed for {message.Id}: {ex}");
        }
    });

    return Results.Created($"/api/conversations/{id}/messages/{message.Id}", message);
});

webApp.MapPut("/api/messages/{messageId:guid}", async (Guid messageId, HttpRequest http, MongoStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var body = await http.ReadFromJsonAsync<EditMessageRequest>(http.HttpContext.RequestAborted);
    var text = body?.Text?.Trim();
    if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) return Results.BadRequest(new { message = "نص الرسالة غير صالح." });
    var result = store.EditMessage(messageId, session.UserId, text);
    if (result.Message is null) return Results.NotFound();
    var recipients = store.GetOtherParticipantUserIds(result.Message.ConversationId, session.UserId);
    try
    {
        await hub.Clients.Groups(recipients.Select(userId => HimoChatHub.UserGroup(userId)))
            .SendAsync("MessageEdited", result.Message, http.HttpContext.RequestAborted);
    }
    catch { }
    return Results.Ok(result.Message);
});

webApp.MapDelete("/api/messages/{messageId:guid}", async (Guid messageId, HttpRequest http, MongoStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var message = store.DeleteMessage(messageId, session.UserId);
    if (message is null) return Results.NotFound();
    var recipients = store.GetOtherParticipantUserIds(message.ConversationId, session.UserId);
    try
    {
        await hub.Clients.Groups(recipients.Select(userId => HimoChatHub.UserGroup(userId)))
            .SendAsync("MessageDeleted", message, http.HttpContext.RequestAborted);
    }
    catch { }
    return Results.Ok(message);
});

webApp.MapPost("/api/conversations/{id:guid}/attachments", async (Guid id, HttpRequest http, MongoStore store, IHubContext<HimoChatHub> hub) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    if (!store.ExistsConversation(id, session.UserId)) return Results.NotFound();
    if (store.IsConversationBlockedForUser(id, session.UserId))
        return Results.Conflict(new { message = "لا يمكن إرسال مرفقات في هذه المحادثة لأن أحد الطرفين حظر الآخر." });
    if (!http.HasFormContentType) return Results.BadRequest(new { message = "صيغة الملف غير صحيحة." });

    var form = await http.ReadFormAsync(http.HttpContext.RequestAborted);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length <= 0) return Results.BadRequest(new { message = "لم يتم اختيار ملف." });
    const long maxBytes = 100L * 1024 * 1024;
    if (file.Length > maxBytes) return Results.BadRequest(new { message = "حجم المرفق يتجاوز 100 ميجابايت." });

    var originalName = Path.GetFileName(file.FileName);
    if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 180) return Results.BadRequest(new { message = "اسم الملف غير صالح." });
    var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim();
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf", "text/plain", "application/zip", "application/octet-stream",
      "audio/mp4", "audio/m4a", "audio/aac", "audio/mpeg", "audio/ogg", "audio/wav",
      "video/mp4", "video/webm", "video/quicktime", "video/3gpp", "video/3gpp2", "video/x-matroska", "video/x-msvideo", "video/mpeg", "video/ogg" };

    // Android MediaPicker may report different video MIME types (or even
    // application/octet-stream) depending on the device/gallery provider.
    // Accept video/* and known video extensions, then normalize the stored
    // type so playback/download remains consistent.
    var extension = Path.GetExtension(originalName);
    var isVideoExtension = extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".3gp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".3g2", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".mpeg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".mpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".ogv", StringComparison.OrdinalIgnoreCase);

    if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || isVideoExtension)
    {
        contentType = extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm"
            : extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ? "video/quicktime"
            : extension.Equals(".3gp", StringComparison.OrdinalIgnoreCase) ? "video/3gpp"
            : extension.Equals(".3g2", StringComparison.OrdinalIgnoreCase) ? "video/3gpp2"
            : extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ? "video/x-matroska"
            : "video/mp4";
    }

    if (!allowed.Contains(contentType)) return Results.BadRequest(new { message = "نوع الملف غير مدعوم حاليًا." });

    var messageId = Guid.NewGuid();
    var displayText = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        ? $"📷 {originalName}"
        : contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            ? $"🎙️ {originalName}"
            : contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? $"🎬 {originalName}"
                : $"📎 {originalName}";

    // Stream directly into MongoDB GridFS. Do not depend on Render's ephemeral disk
    // and do not allocate another 100 MiB byte[] copy for large uploads.
    await using var input = file.OpenReadStream();
    var message = store.AddAttachmentMessage(messageId, id, session.UserId, session.PhoneNumber,
        displayText, originalName, contentType, file.Length, input, http.HttpContext.RequestAborted);
    var recipientIds = store.GetOtherParticipantUserIds(id, session.UserId);

    // Storage succeeded. Realtime delivery and push are best-effort; their failure
    // must not turn a successfully stored upload into an HTTP 500 / duplicate retry.
    try
    {
        await hub.Clients.Groups(recipientIds.Select(userId => HimoChatHub.UserGroup(userId)))
            .SendAsync("MessageReceived", message, CancellationToken.None);
    }
    catch (Exception ex) { Console.Error.WriteLine($"[Himo.SignalR] Attachment delivery failed for {message.Id}: {ex.Message}"); }

    try
    {
        var tokens = store.GetPushTokens(store.GetUnmutedNotificationRecipients(id, recipientIds));
        if (tokens.Count > 0)
        {
            var push = webApp.Services.GetRequiredService<FcmPushService>();
            var acceptedTokens = await push.SendMessageAsync(tokens, session.Name, message.Text,
                message.ConversationId, message.Id, CancellationToken.None,
                isGroup: store.IsGroupConversation(message.ConversationId));
            var deliveredUserIds = store.GetPushTokenOwners(acceptedTokens);
            foreach (var deliveredUserId in deliveredUserIds)
            {
                var receipt = store.MarkMessageDelivered(message.Id, deliveredUserId);
                if (receipt.Changed && receipt.SenderUserId.HasValue)
                    await hub.Clients.Group(HimoChatHub.UserGroup(receipt.SenderUserId.Value))
                        .SendAsync("MessageDeliveryChanged", message.Id, deliveredUserId, "delivered");
            }
        }
    }
    catch (Exception ex) { Console.Error.WriteLine($"[Himo.FCM] Attachment notification failed for {message.Id}: {ex.Message}"); }

    return Results.Created($"/api/conversations/{id}/messages/{message.Id}", message);
});

webApp.MapGet("/api/messages/{messageId:guid}/attachment", (Guid messageId, HttpRequest http, MongoStore store) =>
{
    if (!store.TryGetSession(http, out var session) || session is null) return Results.Unauthorized();
    var attachment = store.OpenAttachment(messageId, session.UserId);
    if (attachment is null) return Results.NotFound();

    // Return a stream so 100 MiB GridFS attachments do not need to be copied into RAM.
    return Results.File(attachment.Stream, attachment.ContentType, attachment.FileName, enableRangeProcessing: true);
});

try
{
    webApp.Run();
}
catch (Exception ex)
{
    webApp.Logger.LogCritical(ex, "[HimoStartup] Himo.Api terminated unexpectedly.");
    throw;
}
finally
{
    Console.WriteLine("[HimoStartup] Himo.Api process stopped.");
}

static bool IsSupportedProfilePhotoType(string contentType) =>
    contentType is "image/jpeg" or "image/png" or "image/webp" or "image/heic" or
        "image/heif" or "image/gif";

static bool IsValidProfilePhotoPayload(byte[] bytes, string contentType)
{
    if (bytes.Length < 12) return false;
    return contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
        "image/webp" => Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
        "image/gif" => Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a",
        "image/heic" or "image/heif" => IsIsoBmffImage(bytes, contentType),
        _ => false
    };
}

static bool IsIsoBmffImage(byte[] bytes, string contentType)
{
    if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 4, 4) != "ftyp") return false;
    var brand = Encoding.ASCII.GetString(bytes, 8, 4).ToLowerInvariant();
    return contentType switch
    {
        "image/heic" => brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1",
        "image/heif" => brand is "heif" or "heic" or "heix" or "mif1" or "msf1",
        _ => false
    };
}

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

static (string? Value, string? Error) NormalizeOptionalContactPhone(string? input)
{
    var value = NormalizeDigits(input?.Trim());
    if (string.IsNullOrWhiteSpace(value)) return (null, null);
    if (value.Length > 25) return (null, "رقم الهاتف طويل جدًا.");
    if (value.Any(ch => !char.IsDigit(ch) && ch is not ('+' or '-' or '(' or ')' or ' ') && !char.IsWhiteSpace(ch)))
        return (null, "رقم الهاتف يحتوي على أحرف غير مسموحة.");
    var digits = new string(value.Where(ch => ch is >= '0' and <= '9').ToArray());
    if (digits.Length is < 7 or > 15) return (null, "أدخل رقم هاتف من 7 إلى 15 رقمًا، أو اتركه فارغًا لأنه اختياري.");
    var firstNonSpace = value.FirstOrDefault(ch => !char.IsWhiteSpace(ch));
    if (value.Count(ch => ch == '+') > 1 || (value.Contains('+') && firstNonSpace != '+'))
        return (null, "ضع علامة + في بداية رقم الهاتف فقط.");
    return (value, null);
}

static bool IsPhoneLookupQuery(string query)
{
    return query.Any(char.IsDigit) && query.All(ch =>
        char.IsDigit(ch) || char.IsWhiteSpace(ch) || ch is '+' or '-' or '(' or ')');
}

static (string? Value, string? Error) NormalizeContactPhoneForMatching(string? input)
{
    var validated = NormalizeOptionalContactPhone(input);
    if (validated.Error is not null || validated.Value is null)
        return validated;

    var digits = new string(NormalizeDigits(validated.Value).Where(char.IsDigit).ToArray());
    if (digits.StartsWith("00", StringComparison.Ordinal))
        digits = digits[2..];
    if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal))
        digits = "966" + digits[1..];
    else if (digits.Length == 9 && digits.StartsWith("5", StringComparison.Ordinal))
        digits = "966" + digits;

    if (digits.Length is < 7 or > 15)
        return (null, "أدخل رقم هاتف صحيحًا من 7 إلى 15 رقمًا.");
    return (digits, null);
}

static bool IsSecretHex(string? value)
    => !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);

static bool IsSha256Hex(string? value) => IsSecretHex(value);

static bool IsValidEmail(string? value)
{
    var email = NormalizeEmail(value);
    if (email.Length is < 5 or > 254 || email.Contains(' ')) return false;
    var at = email.LastIndexOf('@');
    return at > 0 && at < email.Length - 1 && email[(at + 1)..].Contains('.');
}

sealed class HimoChatHub : Hub
{
    private readonly MongoStore _store;
    private readonly FcmPushService _push;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> OnlineUsers = new();

    // Group calls are persistent rooms for the lifetime of the API process. A caller
    // may leave while the remaining members keep talking; a new member can query and
    // join the same CallId later. The room is removed only when its last participant leaves.
    private sealed class ActiveGroupCallRoom
    {
        public required Guid ConversationId { get; init; }
        public required Guid CallId { get; init; }
        public required string Mode { get; init; }
        public required Guid InitiatorUserId { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public HashSet<Guid> ParticipantUserIds { get; } = new();
    }

    private static readonly object GroupCallRoomsGate = new();
    private static readonly Dictionary<Guid, ActiveGroupCallRoom> ActiveGroupCallRooms = new();

    public HimoChatHub(MongoStore store, FcmPushService push)
    {
        _store = store;
        _push = push;
    }

    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public static void ForgetGroupCallRoom(Guid conversationId)
    {
        if (conversationId == Guid.Empty) return;
        lock (GroupCallRoomsGate)
            ActiveGroupCallRooms.Remove(conversationId);
    }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        if (http is null || !_store.TryGetSession(http.Request, out var session) || session is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(session.UserId));
        var becameOnline = OnlineUsers.AddOrUpdate(session.UserId, 1, static (_, count) => count + 1) == 1;
        if (becameOnline)
        {
            foreach (var recipientId in _store.GetPresenceRecipientUserIds(session.UserId))
                await Clients.Group(UserGroup(recipientId)).SendAsync("UserPresenceChanged", session.UserId, true);
        }

        await base.OnConnectedAsync();
    }

    public Task<bool> GetPresence(Guid conversationId)
    {
        var session = GetSessionOrAbort();
        if (session is null || !_store.ExistsConversation(conversationId, session.UserId))
            return Task.FromResult(false);

        foreach (var userId in _store.GetOtherParticipantUserIds(conversationId, session.UserId))
            if (OnlineUsers.TryGetValue(userId, out var count) && count > 0)
                return Task.FromResult(true);
        return Task.FromResult(false);
    }

    public async Task MarkMessageDelivered(Guid messageId)
    {
        var session = GetSessionOrAbort();
        if (session is null) return;
        var result = _store.MarkMessageDelivered(messageId, session.UserId);
        if (result.Changed && result.SenderUserId.HasValue)
            await Clients.Group(UserGroup(result.SenderUserId.Value))
                .SendAsync("MessageDeliveryChanged", messageId, session.UserId, "delivered");
    }

    public Task<GroupCallSessionDto?> GetActiveGroupCall(Guid conversationId)
    {
        var session = GetSessionOrAbort();
        if (session is null || !_store.ExistsConversation(conversationId, session.UserId) ||
            !_store.IsGroupConversation(conversationId))
            return Task.FromResult<GroupCallSessionDto?>(null);

        lock (GroupCallRoomsGate)
        {
            if (!ActiveGroupCallRooms.TryGetValue(conversationId, out var room) ||
                room.ParticipantUserIds.Count == 0)
                return Task.FromResult<GroupCallSessionDto?>(null);
            return Task.FromResult<GroupCallSessionDto?>(ToGroupCallSessionDto(room));
        }
    }

    public async Task<GroupCallSessionDto?> JoinGroupCall(Guid conversationId, Guid callId)
    {
        var session = GetSessionOrAbort();
        if (session is null || callId == Guid.Empty ||
            !_store.ExistsConversation(conversationId, session.UserId) ||
            !_store.IsGroupConversation(conversationId) ||
            _store.IsConversationBlockedForUser(conversationId, session.UserId))
            return null;

        Guid[] existingParticipants;
        GroupCallSessionDto snapshot;
        lock (GroupCallRoomsGate)
        {
            if (!ActiveGroupCallRooms.TryGetValue(conversationId, out var room) || room.CallId != callId)
                return null;

            existingParticipants = room.ParticipantUserIds
                .Where(id => id != session.UserId)
                .ToArray();
            room.ParticipantUserIds.Add(session.UserId);
            snapshot = ToGroupCallSessionDto(room);
        }

        var payload = SerializeGroupCallEnvelope(snapshot);
        // Exchange a synthetic Accept with each already-joined member. This tells
        // both sides to create their missing Mesh PeerConnection without ringing
        // the whole group again. The joining device starts local media after this
        // method returns, so CallService can queue these early peer notifications.
        foreach (var peerUserId in existingParticipants)
        {
            var fromJoiner = new CallSignalMessage(conversationId, session.UserId, "Accept", payload);
            var fromPeer = new CallSignalMessage(conversationId, peerUserId, "Accept", payload);
            await Clients.Group(UserGroup(peerUserId)).SendAsync("CallSignalReceived", fromJoiner);
            await Clients.Group(UserGroup(session.UserId)).SendAsync("CallSignalReceived", fromPeer);
        }

        await BroadcastGroupCallStateAsync(conversationId, session.UserId, snapshot);
        return snapshot;
    }

    public async Task SendCallSignal(Guid conversationId, string type, string? payload, Guid? targetUserId = null)
    {
        var session = GetSessionOrAbort();
        if (session is null || string.IsNullOrWhiteSpace(type) || type.Length > 32)
            return;

        if (!_store.ExistsConversation(conversationId, session.UserId) ||
            _store.IsConversationBlockedForUser(conversationId, session.UserId))
            return;

        var normalizedType = type.Trim();
        var conversationRecipients = _store.GetOtherParticipantUserIds(conversationId, session.UserId);
        // Media negotiation is peer-to-peer: SDP/ICE packets must reach exactly one
        // participant. Lifecycle signals remain broadcast unless explicitly targeted.
        if (targetUserId.HasValue)
        {
            if (targetUserId.Value == session.UserId || !conversationRecipients.Contains(targetUserId.Value))
                return;
        }
        var recipientIds = targetUserId.HasValue
            ? new[] { targetUserId.Value }
            : conversationRecipients;

        // Maintain an independently queryable group-call room. This state is not
        // inferred from ringing notifications: it tracks only the active CallId and
        // the members who have joined. Leave removes one member; an initiator leaving
        // does not terminate the room while other participants remain.
        var updatedGroupCall = UpdateGroupCallRoom(conversationId, session.UserId, normalizedType, payload);
        var message = new CallSignalMessage(conversationId, session.UserId, normalizedType, payload, targetUserId);
        foreach (var recipientId in recipientIds)
            await Clients.Group(UserGroup(recipientId)).SendAsync("CallSignalReceived", message);

        if (updatedGroupCall is not null)
            await BroadcastGroupCallStateAsync(conversationId, session.UserId, updatedGroupCall);

        // Do not await FCM inside the SignalR hub invocation. The caller uses a short
        // signaling timeout, so awaiting Firebase here can cancel End/Reject before
        // the remote phone receives the cancellation; it also adds several seconds
        // to Invite/Answering/Accept and delays WebRTC negotiation.
        if (string.Equals(normalizedType, "Invite", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "Answering", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "Accept", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "Reject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "End", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "Leave", StringComparison.OrdinalIgnoreCase))
        {
            var recipientsSnapshot = recipientIds.ToArray();
            var callerNameSnapshot = session.Name ?? string.Empty;
            var senderUserIdSnapshot = session.UserId;
            var typeSnapshot = normalizedType;
            var payloadSnapshot = payload;
            _ = Task.Run(() => DispatchCallPushAsync(
                recipientsSnapshot, callerNameSnapshot, conversationId, typeSnapshot, payloadSnapshot, senderUserIdSnapshot));
        }
    }

    private GroupCallSessionDto? UpdateGroupCallRoom(
        Guid conversationId,
        Guid senderUserId,
        string signalType,
        string? payload)
    {
        if (!_store.IsGroupConversation(conversationId) ||
            !TryReadGroupCallEnvelope(payload, out var callId, out var mode, out _))
            return null;

        var normalized = signalType.Trim();
        lock (GroupCallRoomsGate)
        {
            ActiveGroupCallRooms.TryGetValue(conversationId, out var room);
            var changed = false;

            if (string.Equals(normalized, "Invite", StringComparison.OrdinalIgnoreCase))
            {
                if (room is not null && room.CallId != callId && room.ParticipantUserIds.Count > 0)
                    throw new HubException("توجد مكالمة جماعية نشطة بالفعل. انضم إلى المكالمة الحالية بدل بدء مكالمة أخرى.");
                if (room is not null && room.CallId == callId && room.InitiatorUserId != senderUserId)
                    throw new HubException("لا يمكن لعضو آخر إعادة إنشاء دعوة المكالمة القائمة.");

                if (room is null || room.CallId != callId)
                {
                    room = new ActiveGroupCallRoom
                    {
                        ConversationId = conversationId,
                        CallId = callId,
                        Mode = mode,
                        // Trust the authenticated SignalR identity, never a caller-supplied
                        // InitiatorUserId in the JSON payload.
                        InitiatorUserId = senderUserId,
                        StartedAt = DateTimeOffset.UtcNow
                    };
                    ActiveGroupCallRooms[conversationId] = room;
                    changed = true;
                }
                changed |= room.ParticipantUserIds.Add(senderUserId);
            }
            else if (room is not null && room.CallId == callId)
            {
                if (string.Equals(normalized, "Accept", StringComparison.OrdinalIgnoreCase))
                {
                    changed = room.ParticipantUserIds.Add(senderUserId);
                }
                else if (string.Equals(normalized, "Leave", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(normalized, "Reject", StringComparison.OrdinalIgnoreCase))
                {
                    changed = room.ParticipantUserIds.Remove(senderUserId);
                }
                else if (string.Equals(normalized, "End", StringComparison.OrdinalIgnoreCase))
                {
                    // Legacy/global End from the original initiator closes the room;
                    // End from another group member is treated as their own departure.
                    if (senderUserId == room.InitiatorUserId)
                    {
                        room.ParticipantUserIds.Clear();
                        changed = true;
                    }
                    else
                    {
                        changed = room.ParticipantUserIds.Remove(senderUserId);
                    }
                }

                if (changed && room.ParticipantUserIds.Count == 0)
                {
                    ActiveGroupCallRooms.Remove(conversationId);
                    return new GroupCallSessionDto(
                        conversationId, callId, mode, room.InitiatorUserId,
                        Array.Empty<Guid>(), room.StartedAt, IsActive: false);
                }
            }

            return changed && room is not null ? ToGroupCallSessionDto(room) : null;
        }
    }

    private async Task BroadcastGroupCallStateAsync(
        Guid conversationId,
        Guid currentUserId,
        GroupCallSessionDto state)
    {
        var memberIds = _store.GetOtherParticipantUserIds(conversationId, currentUserId)
            .Append(currentUserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        foreach (var memberId in memberIds)
            await Clients.Group(UserGroup(memberId)).SendAsync("GroupCallStateChanged", state);
    }

    private static GroupCallSessionDto ToGroupCallSessionDto(ActiveGroupCallRoom room) =>
        new(room.ConversationId, room.CallId, room.Mode, room.InitiatorUserId,
            room.ParticipantUserIds.ToArray(), room.StartedAt, IsActive: true);

    private static string SerializeGroupCallEnvelope(GroupCallSessionDto state) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            callId = state.CallId,
            mode = state.Mode,
            isGroup = true,
            initiatorUserId = state.InitiatorUserId
        });

    private static bool TryReadGroupCallEnvelope(
        string? payload,
        out Guid callId,
        out string mode,
        out Guid initiatorUserId)
    {
        callId = Guid.Empty;
        mode = "audio";
        initiatorUserId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("isGroup", out var groupElement) || groupElement.ValueKind != System.Text.Json.JsonValueKind.True)
                return false;
            if (!root.TryGetProperty("callId", out var callElement) || !Guid.TryParse(callElement.GetString(), out callId) || callId == Guid.Empty)
                return false;
            if (root.TryGetProperty("mode", out var modeElement) &&
                string.Equals(modeElement.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                mode = "video";
            if (root.TryGetProperty("initiatorUserId", out var initiatorElement))
                Guid.TryParse(initiatorElement.GetString(), out initiatorUserId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DispatchCallPushAsync(
        Guid[] recipientIds,
        string callerName,
        Guid conversationId,
        string normalizedType,
        string? payload,
        Guid senderUserId)
    {
        try
        {
            var tokens = _store.GetPushTokens(recipientIds);
            if (tokens.Count == 0) return;

            var mode = "audio";
            var callId = Guid.Empty;
            var isGroup = false;
            var initiatorUserId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    using var callDoc = System.Text.Json.JsonDocument.Parse(payload);
                    var root = callDoc.RootElement;
                    if (root.TryGetProperty("mode", out var modeValue) &&
                        string.Equals(modeValue.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                        mode = "video";
                    if (root.TryGetProperty("callId", out var callIdValue) &&
                        Guid.TryParse(callIdValue.GetString(), out var parsedCallId))
                        callId = parsedCallId;
                    isGroup = root.TryGetProperty("isGroup", out var groupValue) && groupValue.ValueKind == System.Text.Json.JsonValueKind.True;
                    if (root.TryGetProperty("initiatorUserId", out var initiatorValue))
                        Guid.TryParse(initiatorValue.GetString(), out initiatorUserId);
                }
                catch (Exception parseEx)
                {
                    Console.Error.WriteLine($"Call push payload parse failed; type={normalizedType}; error={parseEx.Message}");
                }
            }

            if (string.Equals(normalizedType, "Invite", StringComparison.OrdinalIgnoreCase))
            {
                await _push.SendCallInviteAsync(
                    tokens, callerName, conversationId, mode, callId, CancellationToken.None);
            }
            else if (callId != Guid.Empty)
            {
                var isPeerExit = isGroup &&
                    (string.Equals(normalizedType, "Leave", StringComparison.OrdinalIgnoreCase) ||
                     (senderUserId != initiatorUserId &&
                      string.Equals(normalizedType, "Reject", StringComparison.OrdinalIgnoreCase)));
                await _push.SendCallStateAsync(
                    tokens, conversationId, callId, normalizedType, isPeerExit, senderUserId, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Push is best-effort and runs independently. It must not block or fail
            // the real-time signaling operation that controls audio and navigation.
            Console.Error.WriteLine(
                $"Background call push failed; type={normalizedType}; conversation={conversationId:D}; error={ex}");
        }
    }

    public async Task StartTyping(Guid conversationId)
    {
        var session = GetSessionOrAbort();
        if (session is null || !_store.ExistsConversation(conversationId, session.UserId)) return;
        foreach (var recipientId in _store.GetOtherParticipantUserIds(conversationId, session.UserId))
            await Clients.Group(UserGroup(recipientId)).SendAsync("UserTypingChanged", session.UserId, conversationId, true);
    }

    public async Task StopTyping(Guid conversationId)
    {
        var session = GetSessionOrAbort();
        if (session is null || !_store.ExistsConversation(conversationId, session.UserId)) return;
        foreach (var recipientId in _store.GetOtherParticipantUserIds(conversationId, session.UserId))
            await Clients.Group(UserGroup(recipientId)).SendAsync("UserTypingChanged", session.UserId, conversationId, false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var http = Context.GetHttpContext();
        if (http is not null && _store.TryGetSession(http.Request, out var session) && session is not null)
        {
            var wentOffline = OnlineUsers.AddOrUpdate(session.UserId, 0, static (_, count) => Math.Max(0, count - 1)) == 0;
            if (wentOffline)
            {
                OnlineUsers.TryRemove(session.UserId, out _);
                foreach (var recipientId in _store.GetPresenceRecipientUserIds(session.UserId))
                    await Clients.Group(UserGroup(recipientId)).SendAsync("UserPresenceChanged", session.UserId, false);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private SessionRecord? GetSessionOrAbort()
    {
        var http = Context.GetHttpContext();
        if (http is not null && _store.TryGetSession(http.Request, out var session) && session is not null)
            return session;
        Context.Abort();
        return null;
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

record EmailRegisterRequest(
    string? Email, string? Password, string? Name, string? ContactPhoneNumber = null,
    bool TermsAccepted = false, bool PrivacyAccepted = false,
    string? TermsVersion = null, string? PrivacyVersion = null);
record EmailLoginRequest(string? Email, string? Password, string? DeviceId = null, string? DeviceName = null, string? DeviceType = null);
record StartWebPairingRequest(string? BrowserName, string? QrKeyHash, string? PollSecretHash);
record PollWebPairingRequest(Guid PairingId, string? PollSecret);
record WebPairingQrRequest(string? Payload);
record ApproveWebPairingRequest(Guid PairingId, string? QrKey, string? DeviceName);
record StartMobilePairingRequest(string? QrKeyHash);
record MobilePairingClaimRequest(Guid PairingId, string? QrKey, string? PollSecretHash, string? DeviceId, string? DeviceName);
record MobilePairingPollRequest(Guid PairingId, string? PollSecret);
record MobilePairingDecisionRequest(Guid PairingId);
record VerifyEmailRequest(string? Email, string? Code, string? DeviceId = null, string? DeviceName = null, string? DeviceType = null);
record PasswordResetRequest(string? Email);
record PasswordResetConfirmRequest(string? Email, string? Code, string? NewPassword, string? DeviceId = null, string? DeviceName = null, string? DeviceType = null);
record PasswordResetResult(Guid UserId, string Email, string Name);
enum PasswordResetFailure { None, InvalidCode, RateLimited }
record PushTokenRequest(string? Token);
record UpdateProfileRequest(string? Name, string? Status);
record UpdateUsernameRequest(string? Username);
record EmailChangeRequest(string? Email);
record VerifyEmailChangeRequest(string? Email, string? Code);
record UpdateContactPhoneRequest(string? ContactPhoneNumber, bool PhoneDiscoverable);
record ContactLookupRequest(string[]? PhoneNumbers);
record ContactMatchResponse(Guid Id, string Name, string MatchedPhoneNumber, bool HasProfilePhoto = false, long ProfilePhotoVersion = 0);
record ProfilePhotoUploadRequest(string? Base64Data, string? ContentType);
record CreateConversationRequest(string? Name, Guid? UserId);
record UpdateGroupInfoRequest(string? Name, string? Description);
record GroupPhotoUploadRequest(string? Base64Data, string? ContentType);
record SetGroupMemberAdminRequest(bool IsAdmin);
record JoinGroupByInviteRequest(string? InviteCode, Guid? ExpectedGroupId = null);
record GroupMemberDto(Guid Id, string Name, bool IsAdmin, bool HasProfilePhoto, long ProfilePhotoVersion, DateTimeOffset JoinedAt, string Status = "", string Username = "");
record GroupDetailsDto(Guid Id, string Name, string Description, string InviteCode, bool HasPhoto, long PhotoVersion, bool CanManage, bool CanShareInviteLink, int DisappearingSeconds, IReadOnlyList<GroupMemberDto> Members);
record GroupInvitePreviewDto(Guid Id, string Name, string Description, bool HasPhoto, long PhotoVersion, int MemberCount, bool IsMember, bool RequiresAdminApproval);
record CreateGroupRequest(string? Name, Guid[]? UserIds, int DisappearingSeconds = 0, bool AllMembersCanAddMembers = false, bool AllMembersCanShareInviteLink = false, bool RequireAdminApproval = false, bool AdminsOnlyMessages = false);
record AddGroupMembersRequest(Guid[]? UserIds);
record CallSignalMessage(Guid ConversationId, Guid SenderUserId, string Type, string? Payload, Guid? TargetUserId = null);
record GroupCallSessionDto(Guid ConversationId, Guid CallId, string Mode, Guid InitiatorUserId,
    IReadOnlyList<Guid> ParticipantUserIds, DateTimeOffset StartedAt, bool IsActive = true);
record SendMessageRequest(string? Text, string? ClientMessageId, Guid? ReplyToMessageId);
record ReportConversationRequest(string? Reason);
record ConversationActionRequest(string? Action);
record SetDisappearingSecondsRequest(int Seconds);
record TranslateRequest(string? Text, string? TargetLanguage);
record AuthResponse(string Token, Guid UserId, string Email, string Name);
record UserRecord(Guid Id, string PhoneNumber, string Name);
record EmailVerificationPending(
    string Email, string Name, string PasswordHash, DateTimeOffset ExpiresAt,
    int FailedAttempts, string? ContactPhoneNumber,
    DateTimeOffset TermsAcceptedAt, string TermsVersion,
    DateTimeOffset PrivacyAcceptedAt, string PrivacyVersion);
record SessionRecord(Guid UserId, string PhoneNumber, string Name, DateTimeOffset ExpiresAt, string DeviceId = "", string DeviceName = "جهاز Himo", string DeviceType = "mobile");
sealed class ConversationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string LastMessage { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int UnreadCount { get; init; }
    public Guid? OtherParticipantUserId { get; init; }
    public bool HasProfilePhoto { get; init; }
    public long ProfilePhotoVersion { get; init; }
    public bool IsArchived { get; init; }
    public bool IsMuted { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsGroup { get; init; }

    public ConversationDto()
    {
        Name = string.Empty;
        LastMessage = string.Empty;
    }

    public ConversationDto(
        Guid id,
        string name,
        string lastMessage,
        DateTimeOffset updatedAt,
        int unreadCount,
        Guid? otherParticipantUserId = null,
        bool hasProfilePhoto = false,
        long profilePhotoVersion = 0,
        bool isArchived = false,
        bool isMuted = false,
        bool isBlocked = false,
        bool isGroup = false)
    {
        Id = id;
        Name = name ?? string.Empty;
        LastMessage = lastMessage ?? string.Empty;
        UpdatedAt = updatedAt;
        UnreadCount = unreadCount;
        OtherParticipantUserId = otherParticipantUserId;
        HasProfilePhoto = hasProfilePhoto;
        ProfilePhotoVersion = profilePhotoVersion;
        IsArchived = isArchived;
        IsMuted = isMuted;
        IsBlocked = isBlocked;
        IsGroup = isGroup;
    }
}
sealed class ExpiredMessagesCleanupService(
    IServiceProvider services,
    ILogger<ExpiredMessagesCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not construct MongoStore or perform MongoDB I/O during API startup.
        // Render's liveness endpoint must become available even when Atlas is
        // temporarily unreachable or its connection setting needs correction.
        logger.LogInformation("[HimoExpiry] Cleanup worker scheduled; first run delayed until API startup settles.");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var store = services.GetRequiredService<MongoStore>();
                // MongoDB.Driver calls in MongoStore are synchronous. Keep them
                // off the hosted-service loop so shutdown and other workers remain responsive.
                var deleted = await Task.Run(() => store.DeleteExpiredMessages(), stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("[HimoExpiry] Deleted {Count} expired messages.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Database maintenance failure must never terminate the HTTP API.
                logger.LogWarning(ex, "[HimoExpiry] Expired-message cleanup failed; API remains available.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

record MessageDto(Guid Id, Guid ConversationId, Guid SenderUserId, string SenderPhoneNumber, string Text, DateTimeOffset SentAt, string? AttachmentFileName = null, string? AttachmentContentType = null, long? AttachmentSize = null, string Status = "sent", Guid? ReplyToMessageId = null, string? ReplyToText = null, bool IsEdited = false, string? EditedAt = null, bool IsDeleted = false, DateTimeOffset? ExpiresAt = null);
record AttachmentStreamRecord(string FileName, string ContentType, Stream Stream);
record EditMessageRequest(string Text);
record UserSearchDto(Guid Id, string Email, string Name, bool HasProfilePhoto = false, long ProfilePhotoVersion = 0);
