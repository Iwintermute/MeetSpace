using MeetSpace.Client.Feature.Conference.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeetSpace.Client.Presentation.Navigation;

public sealed class ShellViewModelFactory : IShellViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ShellViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object Create(ShellRegion region)
    {
        return region switch
        {
            ShellRegion.Conference => _serviceProvider.GetRequiredService<ConferencePageViewModel>(),
            _ => _serviceProvider.GetRequiredService<ConferencePageViewModel>()
        };
    }
}