using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Bootstrap
{
    public sealed class BootstrapWarmupService
    {
        private readonly PeerAssignedRouter _peerAssignedRouter;
        private readonly ConnectionStateRouter _connectionStateRouter;
        private readonly ChatInboundRouter _chatInboundRouter;
        private readonly ConferenceInboundRouter _conferenceInboundRouter;

        public BootstrapWarmupService(
            PeerAssignedRouter peerAssignedRouter,
            ConnectionStateRouter connectionStateRouter,
            ChatInboundRouter chatInboundRouter,
            ConferenceInboundRouter conferenceInboundRouter)
        {
            _peerAssignedRouter = peerAssignedRouter;
            _connectionStateRouter = connectionStateRouter;
            _chatInboundRouter = chatInboundRouter;
            _conferenceInboundRouter = conferenceInboundRouter;
        }

        public Task WarmupAsync(CancellationToken cancellationToken = default)
        {
            _ = _peerAssignedRouter;
            _ = _connectionStateRouter;
            _ = _chatInboundRouter;
            _ = _conferenceInboundRouter;
            return Task.CompletedTask;
        }
    }
}