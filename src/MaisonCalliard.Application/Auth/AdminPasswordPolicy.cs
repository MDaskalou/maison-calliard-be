namespace MaisonCalliard.Application.Auth;

public static class AdminPasswordPolicy
{
    public const int MinimumLength = 12;

    public const int MaximumLength = 256;

    public static bool IsStrongEnough(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= MinimumLength
        && password.Length <= MaximumLength
        && password.Trim().Length >= MinimumLength;
}
