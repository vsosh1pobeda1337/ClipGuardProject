using System;
using System.Security.Cryptography;
using ClipGuard.Core;

namespace ClipGuard.Crypto;

public sealed class PasswordService : IPasswordService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 150_000;

    public PasswordRecord CreateRecord(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(password, salt, Iterations);

        return new PasswordRecord
        {
            SaltBase64 = Convert.ToBase64String(salt),
            HashBase64 = Convert.ToBase64String(hash),
            Iterations = Iterations
        };
    }

    public bool Verify(string password, PasswordRecord record)
    {
        var salt = Convert.FromBase64String(record.SaltBase64);
        var expected = Convert.FromBase64String(record.HashBase64);
        var actual = DeriveHash(password, salt, record.Iterations);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] DeriveHash(string password, byte[] salt, int iterations)
    {
        using var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return derive.GetBytes(KeySize);
    }
}
