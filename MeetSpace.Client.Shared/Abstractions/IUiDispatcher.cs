using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace MeetSpace.Client.Shared.Abstractions;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}