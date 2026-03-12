using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Shared.Stores;

public interface IStore<TState> : IReadOnlyStore<TState>
{
    void Set(TState nextState);
    void Update(Func<TState, TState> updater);
}