using System.Globalization;
using System.Text;

namespace MaisonCalliard.DevAdminPasswordReset;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            return await RunAsync(args);
        }
        catch (Exception)
        {
            Console.WriteLine("Återställningen avbröts. Inget ändrades.");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (!TryReadArguments(args, out var expectedDevRef, out var prodRef, out var email))
        {
            Console.WriteLine("Återställningen avbröts. Inget ändrades.");
            return 1;
        }

        var connection = Environment.GetEnvironmentVariable(DevAdminPasswordResetter.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("DevConnection saknas. Inget ändrades.");
            return 1;
        }

        var password = ReadExactPassword(Console.OpenStandardInput());
        if (password is null)
        {
            Console.WriteLine("Återställningen avbröts. Inget ändrades.");
            return 1;
        }

        var outcome = await DevAdminPasswordResetter.ResetAsync(connection, expectedDevRef, prodRef, email, password);
        Console.WriteLine(outcome.Message);
        return outcome.Ok ? 0 : 1;
    }

    private static bool TryReadArguments(string[] args, out string expectedDevRef, out string prodRef, out string email)
    {
        expectedDevRef = "";
        prodRef = "";
        email = "";
        string? expected = null;
        string? prod = null;
        string? selectedEmail = null;

        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            var value = args[index + 1];
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            switch (args[index])
            {
                case "--expected-dev-ref" when expected is null:
                    expected = value;
                    break;
                case "--prod-ref" when prod is null:
                    prod = value;
                    break;
                case "--email" when selectedEmail is null:
                    selectedEmail = value;
                    break;
                default:
                    return false;
            }
        }

        if (expected is null || prod is null || selectedEmail is null)
        {
            return false;
        }

        expectedDevRef = expected;
        prodRef = prod;
        email = selectedEmail;
        return true;
    }

    internal static string? ReadExactPassword(Stream input)
    {
        var header = new List<byte>(8);
        while (true)
        {
            var next = input.ReadByte();
            if (next < 0)
            {
                return null;
            }

            if (next == '\n')
            {
                break;
            }

            if (next == '\r')
            {
                continue;
            }

            if (next < '0' || next > '9' || header.Count == 8)
            {
                return null;
            }

            header.Add((byte)next);
        }

        if (header.Count == 0
            || !int.TryParse(Encoding.ASCII.GetString(header.ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count is < 0 or > 1024)
        {
            return null;
        }

        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = input.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
