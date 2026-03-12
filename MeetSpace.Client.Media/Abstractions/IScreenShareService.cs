using MeetSpace.Client.Media.Models;

namespace MeetSpace.Client.Media.Abstractions;

public interface IScreenShareService
{
    Task<IReadOnlyList<ScreenSourceInfo>> GetSourcesAsync(CancellationToken cancellationToken = default);
}