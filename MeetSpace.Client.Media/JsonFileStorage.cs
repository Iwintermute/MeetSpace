using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Infrastructure.Storage;

public sealed class JsonFileStorage
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public async Task SaveAsync<T>(string filePath, T value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(value, Options);

        using (var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            true))
        using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }

    public async Task<T> LoadAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
            return default(T);

        using (var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            true))
        using (var reader = new StreamReader(stream))
        {
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
    }
}