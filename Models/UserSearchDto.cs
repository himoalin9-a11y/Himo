namespace Himo.Models;

public sealed record UserSearchDto(Guid Id, string Email, string Name)
{
    public string Initial => GetInitial(Name);

    private static string GetInitial(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        return value.Length == 0 ? "H" : value[0].ToString();
    }
}
