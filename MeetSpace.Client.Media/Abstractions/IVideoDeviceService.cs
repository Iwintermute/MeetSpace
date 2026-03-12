using MeetSpace.Client.Media.Models;

namespace MeetSpace.Client.Media.Abstractions;

public interface IVideoDeviceService
{
    Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(CancellationToken cancellationToken = default);
}