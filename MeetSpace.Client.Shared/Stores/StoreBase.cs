
public abstract class StoreBase<TState> : IStore<TState>
{
    private readonly object _sync = new();
    private TState _current;

    protected StoreBase(TState initialState)
    {
        _current = initialState;
    }

    public TState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<TState>? StateChanged;

    public void Set(TState nextState)
    {
        lock (_sync)
        {
            _current = nextState;
        }

        StateChanged?.Invoke(this, nextState);
    }

    public void Update(Func<TState, TState> updater)
    {
        if (updater == null)
            throw new ArgumentNullException(nameof(updater));

        TState next;
        lock (_sync)
        {
            next = updater(_current);
            _current = next;
        }

        StateChanged?.Invoke(this, next);
    }
}