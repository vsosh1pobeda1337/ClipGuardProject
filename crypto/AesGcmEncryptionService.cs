using System;
using System.Security.Cryptography;
using System.Text;
using ClipGuard.Core;

namespace ClipGuard.Crypto;

public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 150_000;

    public EncryptedBuffer Encrypt(string plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt, Iterations);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return new EncryptedBuffer
        {
            CiphertextBase64 = Convert.ToBase64String(cipher),
            NonceBase64 = Convert.ToBase64String(nonce),
            TagBase64 = Convert.ToBase64String(tag),
            SaltBase64 = Convert.ToBase64String(salt),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public string Decrypt(EncryptedBuffer encrypted, string password)
    {
        var salt = Convert.FromBase64String(encrypted.SaltBase64);
        var nonce = Convert.FromBase64String(encrypted.NonceBase64);
        var tag = Convert.FromBase64String(encrypted.TagBase64);
        var cipher = Convert.FromBase64String(encrypted.CiphertextBase64);

        var key = DeriveKey(password, salt, Iterations);
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key);
        aes.Decrypt(nonce, cipher, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        using var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return derive.GetBytes(KeySize);
    }
}
