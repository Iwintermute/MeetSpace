using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Application.Abstractions.Navigation;

public interface INavigationService
{
    Task NavigateAsync(string route, CancellationToken cancellationToken = default);
}