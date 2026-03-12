using MeetSpace.Client.Media.Abstractions;
using MeetSpace.Client.Media.Models;

namespace MeetSpace.Client.Media.Services;

public sealed class NullMediaEngine : IMediaEngine, IAudioDeviceService, IVideoDeviceService, IScreenShareService
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<AudioInputDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AudioInputDeviceInfo>>(new[]
        {
            new AudioInputDeviceInfo("default-mic", "Default microphone", true)
        });

    public Task<IReadOnlyList<AudioOutputDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AudioOutputDeviceInfo>>(new[]
        {
            new AudioOutputDeviceInfo("default-speaker", "Default speakers", true)
        });

    public Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CameraDeviceInfo>>(new[]
        {
            new CameraDeviceInfo("default-camera", "Default camera", true)
        });

    public Task<IReadOnlyList<ScreenSourceInfo>> GetSourcesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScreenSourceInfo>>(new[]
        {
            new ScreenSourceInfo("display-1", "Primary display", true, false)
        });
}