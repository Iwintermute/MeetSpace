using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.App.Conference;

public sealed class ConferenceStore : StoreBase<ConferenceViewState>
{
    public ConferenceStore() : base(ConferenceViewState.Empty)
    {
    }
}