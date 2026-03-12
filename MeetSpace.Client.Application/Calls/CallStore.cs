using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.Application.Calls;

public sealed class CallStore : StoreBase<CallSessionState>
{
    public CallStore() : base(CallSessionState.Empty)
    {
    }
}