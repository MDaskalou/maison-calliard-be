using System.Security.Cryptography;
using System.Text;

namespace MaisonCalliard.Application.Auth;

public static class RefreshTokenProtector
{
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
