namespace MeetSpace.Client.Shared.Abstractions;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}