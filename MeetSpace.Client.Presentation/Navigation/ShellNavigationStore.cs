using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.Presentation.Navigation;

public sealed class ShellNavigationStore : StoreBase<ShellNavigationState>
{
    public ShellNavigationStore() : base(ShellNavigationState.Empty)
    {
    }
}