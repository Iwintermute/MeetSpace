using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.App.Calls;

public sealed class CallStore : StoreBase<CallSessionState>
{
    public CallStore() : base(CallSessionState.Empty)
    {
    }
}