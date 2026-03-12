namespace MeetSpace.Client.Presentation.Navigation;

public sealed class ShellNavigationService : IShellNavigationService
{
    private readonly ShellNavigationStore _store;
    private readonly IShellViewModelFactory _factory;

    public ShellNavigationService(
        ShellNavigationStore store,
        IShellViewModelFactory factory)
    {
        _store = store;
        _factory = factory;
    }

    public Task NavigateAsync(ShellRegion region, CancellationToken cancellationToken = default)
    {
        var viewModel = _factory.Create(region);
        _store.Set(new ShellNavigationState(region, viewModel));
        return Task.CompletedTask;
    }
}