namespace MeetSpace.Client.Media.Abstractions;

public interface IMediaEngine
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}