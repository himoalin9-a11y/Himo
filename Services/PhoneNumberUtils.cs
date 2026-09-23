namespace Himo.Services;

public static class PhoneNumberUtils
{
    public static string Normalize(string? value)
    {
        var input = value ?? string.Empty;
        var chars = new List<char>(input.Length);
        foreach (var ch in input)
        {
            if (ch >= '0' && ch <= '9')
            {
                chars.Add(ch);
                continue;
            }

            if (ch >= '٠' && ch <= '٩')
            {
                chars.Add((char)('0' + (ch - '٠')));
                continue;
            }

            if (ch >= '۰' && ch <= '۹')
                chars.Add((char)('0' + (ch - '۰')));
        }
        return new string(chars.ToArray());
    }

    public static string NormalizeDigits(string? value)
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

    public static bool IsValidVerificationCode(string? value)
    {
        var code = NormalizeDigits(value?.Trim());
        if (code.Length != 6) return false;
        for (var i = 0; i < code.Length; i++)
            if (code[i] < '0' || code[i] > '9') return false;
        return true;
    }

    public static bool IsValid(string? value)
    {
        var original = value?.Trim() ?? string.Empty;
        var normalized = Normalize(original);
        if (string.IsNullOrWhiteSpace(original) || normalized.Length is < 7 or > 15)
            return false;

        var sawDigit = false;
        foreach (var ch in original)
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '(' or ')') continue;
            if (ch == '+')
            {
                if (sawDigit) return false;
                continue;
            }

            if ((ch >= '0' && ch <= '9') || (ch >= '٠' && ch <= '٩') || (ch >= '۰' && ch <= '۹'))
            {
                sawDigit = true;
                continue;
            }

            return false;
        }

        return sawDigit;
    }
}
