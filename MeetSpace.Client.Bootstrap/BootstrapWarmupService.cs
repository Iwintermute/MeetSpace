using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Bootstrap;

public sealed class BootstrapWarmupService
{
    private readonly SessionInboundRouter _sessionInboundRouter;
    private readonly ChatInboundRouter _chatInboundRouter;
    private readonly ConferenceInboundRouter _conferenceInboundRouter;
    private readonly CallInboundRouter _callInboundRouter;

    public BootstrapWarmupService(
        SessionInboundRouter sessionInboundRouter,
        ChatInboundRouter chatInboundRouter,
        ConferenceInboundRouter conferenceInboundRouter,
        CallInboundRouter callInboundRouter)
    {
        _sessionInboundRouter = sessionInboundRouter;
        _chatInboundRouter = chatInboundRouter;
        _conferenceInboundRouter = conferenceInboundRouter;
        _callInboundRouter = callInboundRouter;
    }

    public Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _ = _sessionInboundRouter;
        _ = _chatInboundRouter;
        _ = _conferenceInboundRouter;
        _ = _callInboundRouter;
        return Task.CompletedTask;
    }
}