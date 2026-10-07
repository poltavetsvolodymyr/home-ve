using System.Security.Cryptography;
using System.Text;

namespace HomeBackend.Security;

/// <summary>Format: pbkdf2-sha256$iterations$salt(base64)$hash(base64)</summary>
public static class PasswordHash
{
    private const int Iterations = 600_000;

    public static string Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts is not ["pbkdf2-sha256", var iter, var salt, var hash] || !int.TryParse(iter, out var iterations))
            return false;

        var expected = Convert.FromBase64String(hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
