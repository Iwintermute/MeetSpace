namespace MeetSpace.Client.App.Calls;

public interface IAudioBridgeHost : IDisposable
{
    event EventHandler<string>? MessageReceived;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task PostJsonAsync(string json, CancellationToken cancellationToken = default);
}