using MeetSpace.Client.Media.Models;

namespace MeetSpace.Client.Media.Abstractions;

public interface IAudioDeviceService
{
    Task<IReadOnlyList<AudioInputDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AudioOutputDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default);
}