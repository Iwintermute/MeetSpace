using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.Application.Conference;

public sealed class ConferenceStore : StoreBase<ConferenceViewState>
{
    public ConferenceStore() : base(ConferenceViewState.Empty)
    {
    }
}