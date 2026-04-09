using MeetSpace.Client.App.Conference;
using MeetSpace.Client.Shared.Results;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Bootstrap
{
    public sealed class RealtimeStartupService
    {
        private readonly ConferenceCoordinator _conferenceCoordinator;
        private int _initialized;

        public RealtimeStartupService(ConferenceCoordinator conferenceCoordinator)
        {
            _conferenceCoordinator = conferenceCoordinator;
        }

        public async Task<Result> EnsureConnectedAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 1)
                return Result.Success();

            var result = await _conferenceCoordinator.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
                Interlocked.Exchange(ref _initialized, 0);

            return result;
        }
    }
}