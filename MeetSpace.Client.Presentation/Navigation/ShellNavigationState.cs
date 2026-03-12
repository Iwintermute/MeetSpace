namespace MeetSpace.Client.Presentation.Navigation;

public sealed record ShellNavigationState(
    ShellRegion CurrentRegion,
    object? CurrentViewModel)
{
    public static ShellNavigationState Empty { get; } = new(
        ShellRegion.None,
        null);
}