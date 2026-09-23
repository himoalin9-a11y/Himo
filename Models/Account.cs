namespace Himo.Models;

public sealed class Account
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "مستخدم Himo";
    public string Initial => GetInitial(Name);

    private static string GetInitial(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        return value.Length == 0 ? "H" : value[0].ToString();
    }
}
