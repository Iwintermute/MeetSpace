using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace MeetSpace.Client.Shared.Stores;


public interface IReadOnlyStore<TState>
{
    TState Current { get; }
    event EventHandler<TState>? StateChanged;
}