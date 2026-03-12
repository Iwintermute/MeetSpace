using MeetSpace.Client.Application.Abstractions.Navigation;

namespace MeetSpace.Client.Application.Coordinators;

public sealed class ShellCoordinator
{
    private readonly INavigationService _navigationService;

    public ShellCoordinator(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public Task OpenHomeAsync(CancellationToken cancellationToken = default)
        => _navigationService.NavigateAsync("home", cancellationToken);
}