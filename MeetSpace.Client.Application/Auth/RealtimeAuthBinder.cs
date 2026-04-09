using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.App.Auth;

public sealed class RealtimeAuthBinder
{
    private readonly IRealtimeGateway _gateway;
    private readonly AuthSessionStore _authStore;

    public RealtimeAuthBinder(IRealtimeGateway gateway, AuthSessionStore authStore)
    {
        _gateway = gateway;
        _authStore = authStore;
    }

    public async Task BindAsync(CancellationToken cancellationToken = default)
    {
        var auth = _authStore.Current;
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken))
            throw new InvalidOperationException("No authenticated session.");

        await _gateway.SendAsync(new FeatureRequestEnvelope
        {
            Object = "auth",
            Agent = "session",
            Action = "bind_session",
            Ctx = new Dictionary<string, object?>
            {
                ["accessToken"] = auth.AccessToken,
                ["deviceId"] = "uwp-desktop"
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}