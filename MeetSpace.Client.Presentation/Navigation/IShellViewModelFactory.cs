namespace MeetSpace.Client.Presentation.Navigation;

public interface IShellViewModelFactory
{
    object Create(ShellRegion region);
}