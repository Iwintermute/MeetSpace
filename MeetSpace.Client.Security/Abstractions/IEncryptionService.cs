
using MeetSpace.Client.Security.Models;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Security.Abstractions;

public interface IEncryptionService
{
    Task<EncryptedPayload> EncryptAsync(string plaintext, byte[] key, CancellationToken cancellationToken = default);
    Task<string> DecryptAsync(EncryptedPayload payload, byte[] key, CancellationToken cancellationToken = default);
}