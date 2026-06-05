using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeetSpace.Client.Security.Abstractions;
using MeetSpace.Client.Security.Models;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace MeetSpace.Client.Security.Services;

public sealed class Aes256EnvelopeEncryptionService : IEncryptionService
{
    private const int Aes256KeySizeBytes = 32;
    private const int GcmNonceSizeBytes = 12;
    private const int GcmTagSizeBytes = 16;
    private const string Aes256GcmAlgorithm = "AES-256-GCM";
    public Task<EncryptedPayload> EncryptAsync(string plaintext, byte[] key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateInputs(plaintext, key);

        var nonce = new byte[GcmNonceSizeBytes];
        var tag = new byte[GcmTagSizeBytes];
        FillRandom(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var gcm = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(key), GcmTagSizeBytes * 8, nonce);
        gcm.Init(true, parameters);

        var cipherWithTag = new byte[gcm.GetOutputSize(plaintextBytes.Length)];
        var outputLength = gcm.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, cipherWithTag, 0);
        outputLength += gcm.DoFinal(cipherWithTag, outputLength);

        var cipherLength = outputLength - GcmTagSizeBytes;
        var cipherBytes = new byte[cipherLength];
        Buffer.BlockCopy(cipherWithTag, 0, cipherBytes, 0, cipherLength);
        Buffer.BlockCopy(cipherWithTag, cipherLength, tag, 0, GcmTagSizeBytes);

        var payload = new EncryptedPayload(
            Aes256GcmAlgorithm,
            Convert.ToBase64String(cipherBytes),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nonceSizeBytes"] = GcmNonceSizeBytes.ToString(),
                ["tagSizeBytes"] = GcmTagSizeBytes.ToString()
            });

        return Task.FromResult(payload);
    }

    public Task<string> DecryptAsync(EncryptedPayload payload, byte[] key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (key.Length != Aes256KeySizeBytes)
            throw new ArgumentException("AES-256 key must be 32 bytes long.", nameof(key));

        var isGcmPayload =
            string.Equals(payload.Algorithm, Aes256GcmAlgorithm, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(payload.TagBase64);

        if (isGcmPayload)
        {
            var nonce = Convert.FromBase64String(payload.IvBase64);
            var tag = Convert.FromBase64String(payload.TagBase64 ?? string.Empty);
            var cipherBytes = Convert.FromBase64String(payload.CipherTextBase64);
            var cipherWithTag = new byte[cipherBytes.Length + tag.Length];
            Buffer.BlockCopy(cipherBytes, 0, cipherWithTag, 0, cipherBytes.Length);
            Buffer.BlockCopy(tag, 0, cipherWithTag, cipherBytes.Length, tag.Length);

            var gcm = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(key), tag.Length * 8, nonce);
            gcm.Init(false, parameters);

            try
            {
                var plainBytes = new byte[gcm.GetOutputSize(cipherWithTag.Length)];
                var plainLength = gcm.ProcessBytes(cipherWithTag, 0, cipherWithTag.Length, plainBytes, 0);
                plainLength += gcm.DoFinal(plainBytes, plainLength);
                return Task.FromResult(Encoding.UTF8.GetString(plainBytes, 0, plainLength));
            }
            catch (InvalidCipherTextException ex)
            {
                throw new CryptographicException("AES-GCM authentication failed.", ex);
            }
        }

        return DecryptLegacyCbcAsync(payload, key);
    }

    private static Task<string> DecryptLegacyCbcAsync(EncryptedPayload payload, byte[] key)
    {
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

    private static void ValidateInputs(string plaintext, byte[] key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (plaintext == null)
            throw new ArgumentNullException(nameof(plaintext));

        if (key.Length != Aes256KeySizeBytes)
            throw new ArgumentException("AES-256 key must be 32 bytes long.", nameof(key));
    }

    private static void FillRandom(byte[] buffer)
    {
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(buffer);
        }
    }
}