using MeetSpace.Client.Shared.Stores;

public interface IStore<TState> : IReadOnlyStore<TState>
{
    void Set(TState nextState);
    void Update(Func<TState, TState> updater);
}