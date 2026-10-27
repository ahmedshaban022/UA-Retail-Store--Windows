using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace StoreFlow.Application.Security;

/// <summary>
/// Hashes and verifies passwords.
/// </summary>
/// <remarks>
/// Argon2id is the primary scheme, using the OWASP-recommended parameters (m = 47 104 KiB, t = 1,
/// p = 1). PBKDF2-HMAC-SHA256 is accepted for hashes written by an earlier version or imported from
/// another system; such a password is transparently re-hashed with Argon2id the next time its owner
/// signs in successfully. No password is ever stored in plaintext or reversibly encrypted
/// (PRD §3).
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes a password with the primary scheme.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a password against a stored hash. The result says whether the password matched and
    /// whether the stored hash should be upgraded.
    /// </summary>
    PasswordVerificationResult Verify(string password, string storedHash);
}

/// <summary>The outcome of verifying a password.</summary>
/// <param name="IsVerified">True when the password matched.</param>
/// <param name="NeedsRehash">True when the stored hash uses an older scheme and should be upgraded.</param>
public readonly record struct PasswordVerificationResult(bool IsVerified, bool NeedsRehash);

/// <summary>The default password hasher.</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>Argon2id memory cost in kibibytes (47 MiB), per the OWASP guidance.</summary>
    public const int Argon2MemoryKiB = 47_104;

    /// <summary>Argon2id time cost.</summary>
    public const int Argon2Iterations = 1;

    /// <summary>Argon2id parallelism.</summary>
    public const int Argon2Parallelism = 1;

    /// <summary>PBKDF2 iteration count, used for verifying legacy hashes.</summary>
    public const int Pbkdf2Iterations = 600_000;

    private const string Argon2Prefix = "argon2id";
    private const string Pbkdf2Prefix = "pbkdf2";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = ComputeArgon2(password, salt, Argon2MemoryKiB, Argon2Iterations, Argon2Parallelism);

        return string.Join(
            '$',
            Argon2Prefix,
            Argon2MemoryKiB.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Argon2Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Argon2Parallelism.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public PasswordVerificationResult Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return new PasswordVerificationResult(false, false);
        }

        string[] parts = storedHash.Split('$', StringSplitOptions.None);

        if (parts.Length == 6 && parts[0] == Argon2Prefix)
        {
            return VerifyArgon2(password, parts);
        }

        if (parts.Length == 5 && parts[0] == Pbkdf2Prefix)
        {
            return VerifyPbkdf2(password, parts);
        }

        // An unknown format never authenticates, but it is still marked for rehash so a user with a
        // legacy account can be migrated the moment a valid password is presented.
        return new PasswordVerificationResult(false, false);
    }

    private static PasswordVerificationResult VerifyArgon2(string password, string[] parts)
    {
        if (!int.TryParse(parts[1], out int memory) ||
            !int.TryParse(parts[2], out int iterations) ||
            !int.TryParse(parts[3], out int parallelism))
        {
            return new PasswordVerificationResult(false, false);
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[4]);
            expected = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return new PasswordVerificationResult(false, false);
        }

        byte[] actual = ComputeArgon2(password, salt, memory, iterations, parallelism);

        bool verified = CryptographicOperations.FixedTimeEquals(actual, expected);
        bool needsRehash = memory != Argon2MemoryKiB ||
                           iterations != Argon2Iterations ||
                           parallelism != Argon2Parallelism;

        return new PasswordVerificationResult(verified, needsRehash);
    }

    private static PasswordVerificationResult VerifyPbkdf2(string password, string[] parts)
    {
        if (!int.TryParse(parts[2], out int iterations))
        {
            return new PasswordVerificationResult(false, false);
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return new PasswordVerificationResult(false, false);
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return new PasswordVerificationResult(
            CryptographicOperations.FixedTimeEquals(actual, expected),
            NeedsRehash: true);
    }

    private static byte[] ComputeArgon2(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(32);
    }
}
