using System.Security.Cryptography;
using System.Text;

namespace MaisonCalliard.Infrastructure.Services;

internal static class LoginCodeHasher
{
    public static string CreateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string pepper, Guid challengeId, string code)
    {
        var payload = Encoding.UTF8.GetBytes(challengeId.ToString("N") + ":" + code);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), payload));
    }

    public static bool Matches(string pepper, Guid challengeId, string code, string expectedHash)
    {
        try
        {
            var actual = Convert.FromHexString(Hash(pepper, challengeId, code));
            var expected = Convert.FromHexString(expectedHash);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
