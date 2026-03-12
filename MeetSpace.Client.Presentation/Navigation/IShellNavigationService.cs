namespace MeetSpace.Client.Presentation.Navigation;

public interface IShellNavigationService
{
    Task NavigateAsync(ShellRegion region, CancellationToken cancellationToken = default);
}