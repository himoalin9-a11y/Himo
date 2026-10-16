using System.Net.Http.Json;

sealed class EmailVerificationService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailVerificationService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendPasswordResetCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        await SendAsync(email, "إعادة تعيين كلمة مرور Himo", $"رمز إعادة تعيين كلمة المرور في Himo هو: {code}", code, "إذا لم تطلب إعادة تعيين كلمة المرور، تجاهل هذه الرسالة.", cancellationToken);
    }

    private async Task SendAsync(string email, string subject, string bodyText, string code, string footer, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("HIMO_EMAIL_API_KEY")?.Trim();
        var from = Environment.GetEnvironmentVariable("HIMO_EMAIL_FROM")?.Trim();
        var fromName = Environment.GetEnvironmentVariable("HIMO_EMAIL_FROM_NAME")?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from))
            throw new EmailProviderNotConfiguredException("خدمة البريد الإلكتروني غير مهيأة على الخادم. أضف HIMO_EMAIL_API_KEY و HIMO_EMAIL_FROM في إعدادات Render.");
        if (string.IsNullOrWhiteSpace(fromName)) fromName = "Himo";
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("api-key", apiKey);
        var payload = new { sender = new { name = fromName, email = from }, to = new[] { new { email } }, subject, htmlContent = $"<!doctype html><html lang=\"ar\" dir=\"rtl\"><body style=\"font-family:Arial,sans-serif;background:#f4f7fb;padding:24px\"><div style=\"max-width:520px;margin:auto;background:#fff;border-radius:18px;padding:28px;text-align:center\"><h2 style=\"color:#1769e0\">Himo</h2><p>{System.Net.WebUtility.HtmlEncode(bodyText)}</p><div style=\"font-size:34px;font-weight:700;letter-spacing:8px;margin:24px 0;color:#1769e0\">{code}</div><p>الرمز صالح لمدة 10 دقائق.</p><p style=\"color:#777;font-size:12px\">{System.Net.WebUtility.HtmlEncode(footer)}</p></div></body></html>", textContent = $"{bodyText}\nالرمز صالح لمدة 10 دقائق.\n{footer}" };
        using var response = await client.PostAsJsonAsync("https://api.brevo.com/v3/smtp/email", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new EmailProviderException((int)response.StatusCode, responseText);
        }
    }

    public async Task SendCodeAsync(string email, string name, string code, CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("HIMO_EMAIL_API_KEY")?.Trim();
        var from = Environment.GetEnvironmentVariable("HIMO_EMAIL_FROM")?.Trim();
        var fromName = Environment.GetEnvironmentVariable("HIMO_EMAIL_FROM_NAME")?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from))
            throw new EmailProviderNotConfiguredException("خدمة البريد الإلكتروني غير مهيأة على الخادم. أضف HIMO_EMAIL_API_KEY و HIMO_EMAIL_FROM في إعدادات Render.");

        if (string.IsNullOrWhiteSpace(fromName)) fromName = "Himo";

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("api-key", apiKey);

        var payload = new
        {
            sender = new { name = fromName, email = from },
            to = new[] { new { email, name } },
            subject = "رمز التحقق من Himo",
            htmlContent = $"<!doctype html><html lang=\"ar\" dir=\"rtl\"><body style=\"font-family:Arial,sans-serif;background:#f4f7fb;padding:24px\"><div style=\"max-width:520px;margin:auto;background:#fff;border-radius:18px;padding:28px;text-align:center\"><h2 style=\"color:#1769e0\">مرحبًا {System.Net.WebUtility.HtmlEncode(name)}</h2><p>رمز التحقق الخاص بحسابك في Himo هو:</p><div style=\"font-size:34px;font-weight:700;letter-spacing:8px;margin:24px 0;color:#1769e0\">{code}</div><p>الرمز صالح لمدة 10 دقائق.</p><p style=\"color:#777;font-size:12px\">إذا لم تطلب إنشاء حساب Himo، تجاهل هذه الرسالة.</p></div></body></html>",
            textContent = $"مرحبًا {name}\nرمز التحقق الخاص بحسابك في Himo هو: {code}\nالرمز صالح لمدة 10 دقائق."
        };

        using var response = await client.PostAsJsonAsync("https://api.brevo.com/v3/smtp/email", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new EmailProviderException((int)response.StatusCode, responseText);
        }
    }
}

sealed class EmailProviderNotConfiguredException : InvalidOperationException
{
    public EmailProviderNotConfiguredException(string message) : base(message) { }
}

sealed class EmailProviderException : InvalidOperationException
{
    public int StatusCode { get; }
    public string ProviderResponse { get; }

    public EmailProviderException(int statusCode, string providerResponse)
        : base($"Brevo rejected the email request ({statusCode}).")
    {
        StatusCode = statusCode;
        ProviderResponse = providerResponse;
    }
}
