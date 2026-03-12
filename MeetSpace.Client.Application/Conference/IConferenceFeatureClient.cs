using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.App.Conference;

public interface IConferenceFeatureClient
{
    Task CreateConferenceAsync(string conferenceId, string? clientRequestId = null, CancellationToken cancellationToken = default);
    Task GetConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task JoinConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task LeaveConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task ListMembersAsync(string conferenceId, CancellationToken cancellationToken = default);
}
