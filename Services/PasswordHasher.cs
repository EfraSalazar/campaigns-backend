using System.Security.Cryptography;
using System.Text;

namespace EventCampaignSystem.Services;

/// <summary>
/// Hashing de contraseñas con PBKDF2-HMAC-SHA256, compatible hacia atrás con el
/// esquema heredado SHA256(password+salt).
///
/// IMPORTANTE: esta lógica es una COPIA IDÉNTICA de registration-backend
/// (Services/PasswordHasher.cs) porque ambos servicios comparten la tabla Users.
/// Campaigns solo VERIFICA (lee ambos formatos); el re-hasheo a PBKDF2 lo hace
/// registration al iniciar sesión. Si cambias el formato o los parámetros, cámbialos
/// también en registration-backend.
/// </summary>
public static class PasswordHasher
{
    private const string Pbkdf2Prefix = "pbkdf2_sha256$";
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string GenerateSalt()
    {
        var salt = new byte[SaltBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return Convert.ToBase64String(salt);
    }

    public static string Hash(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
        return $"{Pbkdf2Prefix}{Iterations}${Convert.ToBase64String(hash)}";
    }

    public static bool NeedsUpgrade(string storedHash)
    {
        return storedHash == null || !storedHash.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal);
    }

    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(storedHash) || storedSalt == null)
        {
            return false;
        }

        if (storedHash.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var iterations))
            {
                return false;
            }

            byte[] expected;
            byte[] saltBytes;
            try
            {
                expected = Convert.FromBase64String(parts[2]);
                saltBytes = Convert.FromBase64String(storedSalt);
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        // Esquema heredado: SHA256(password + salt) en base64.
        using var sha256 = SHA256.Create();
        var legacy = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(password + storedSalt)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(legacy),
            Encoding.UTF8.GetBytes(storedHash));
    }
}
