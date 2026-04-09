using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeetSpace.Client.Security.Abstractions;
using MeetSpace.Client.Security.Models;

namespace MeetSpace.Client.Security.Services;

public sealed class Aes256EnvelopeEncryptionService : IEncryptionService
{
    public Task<EncryptedPayload> EncryptAsync(string plaintext, byte[] key, CancellationToken cancellationToken = default)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (plaintext == null)
            throw new ArgumentNullException(nameof(plaintext));

        if (key.Length != 32)
            throw new ArgumentException("AES-256 key must be 32 bytes long.", nameof(key));

        var iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var encryptor = aes.CreateEncryptor())
            {
                var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                var cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

                var payload = new EncryptedPayload(
                    "AES-256-CBC",
                    Convert.ToBase64String(cipherBytes),
                    Convert.ToBase64String(iv),
                    null);

                return Task.FromResult(payload);
            }
        }
    }

    public Task<string> DecryptAsync(EncryptedPayload payload, byte[] key, CancellationToken cancellationToken = default)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (key.Length != 32)
            throw new ArgumentException("AES-256 key must be 32 bytes long.", nameof(key));

        var iv = Convert.FromBase64String(payload.IvBase64);
        var cipherBytes = Convert.FromBase64String(payload.CipherTextBase64);

        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var decryptor = aes.CreateDecryptor())
            {
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return Task.FromResult(Encoding.UTF8.GetString(plainBytes));
            }
        }
    }
}