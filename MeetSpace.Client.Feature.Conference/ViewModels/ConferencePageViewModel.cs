using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Conference;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.Feature.Conference.ViewModels;

public partial class ConferencePageViewModel : ObservableObject, IDisposable
{
    private readonly ConferenceCoordinator _coordinator;
    private readonly ConferenceStore _conferenceStore;
    private readonly SessionStore _sessionStore;
    private readonly IRealtimeGateway _gateway;

    [ObservableProperty]
    private string _serverEndpoint = "ws://127.0.0.1:9002";

    [ObservableProperty]
    private string _conferenceId = "conference-001";

    [ObservableProperty]
    private string _trustedPeer = "-";

    [ObservableProperty]
    private string _connectionState = "Disconnected";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private string _activeConferenceId = "-";

    [ObservableProperty]
    private string _ownerPeerId = "-";

    [ObservableProperty]
    private bool _isClosed;

    [ObservableProperty]
    private ulong _revision;

    [ObservableProperty]
    private int _memberCount;

    [ObservableProperty]
    private string _membersText = "No members";

    [ObservableProperty]
    private string _lastServerMessage = string.Empty;

    public ConferencePageViewModel(
        ConferenceCoordinator coordinator,
        ConferenceStore conferenceStore,
        SessionStore sessionStore,
        IRealtimeGateway gateway)
    {
        _coordinator = coordinator;
        _conferenceStore = conferenceStore;
        _sessionStore = sessionStore;
        _gateway = gateway;

        _conferenceStore.StateChanged += OnConferenceStateChanged;
        _sessionStore.StateChanged += OnSessionStateChanged;
        _gateway.EnvelopeReceived += OnEnvelopeReceived;

        ApplyConferenceState(_conferenceStore.Current);
        ApplySessionState(_sessionStore.Current);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var result = await _coordinator.ConnectAsync(ServerEndpoint);
        if (result.IsFailure)
            LastError = result.Error?.Message ?? "Connection failed.";
    }

    [RelayCommand]
    private Task DisconnectAsync()
        => _coordinator.DisconnectAsync();

    [RelayCommand]
    private async Task CreateConferenceAsync()
    {
        var result = await _coordinator.CreateConferenceAsync(ConferenceId);
        if (result.IsFailure)
            LastError = result.Error?.Message ?? "Create conference failed.";
    }

    [RelayCommand]
    private async Task JoinConferenceAsync()
    {
        var result = await _coordinator.JoinConferenceAsync(ConferenceId);
        if (result.IsFailure)
            LastError = result.Error?.Message ?? "Join conference failed.";
    }

    [RelayCommand]
    private async Task ListMembersAsync()
    {
        var result = await _coordinator.ListMembersAsync(ConferenceId);
        if (result.IsFailure)
            LastError = result.Error?.Message ?? "List members failed.";
    }

    [RelayCommand]
    private async Task GetConferenceAsync()
    {
        if (string.IsNullOrWhiteSpace(ConferenceId))
        {
            LastError = "Conference ID must not be empty.";
            return;
        }

        IsBusy = true;
        LastError = string.Empty;

        try
        {
            await _gateway.SendAsync(new FeatureRequestEnvelope
            {
                Object = ConferenceProtocol.Object,
                Agent = ConferenceProtocol.Agents.Lifecycle,
                Action = ConferenceProtocol.Actions.GetConference,
                Ctx = new Dictionary<string, object?>
                {
                    ["conferenceId"] = ConferenceId
                }
            });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LeaveConferenceAsync()
    {
        if (string.IsNullOrWhiteSpace(ConferenceId))
        {
            LastError = "Conference ID must not be empty.";
            return;
        }

        IsBusy = true;
        LastError = string.Empty;

        try
        {
            await _gateway.SendAsync(new FeatureRequestEnvelope
            {
                Object = ConferenceProtocol.Object,
                Agent = ConferenceProtocol.Agents.Membership,
                Action = ConferenceProtocol.Actions.LeaveConference,
                Ctx = new Dictionary<string, object?>
                {
                    ["conferenceId"] = ConferenceId
                }
            });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshConferenceAsync()
    {
        if (string.IsNullOrWhiteSpace(ConferenceId))
        {
            LastError = "Conference ID must not be empty.";
            return;
        }

        IsBusy = true;
        LastError = string.Empty;

        try
        {
            await _gateway.SendAsync(new FeatureRequestEnvelope
            {
                Object = ConferenceProtocol.Object,
                Agent = ConferenceProtocol.Agents.Lifecycle,
                Action = ConferenceProtocol.Actions.GetConference,
                Ctx = new Dictionary<string, object?>
                {
                    ["conferenceId"] = ConferenceId
                }
            });

            await _gateway.SendAsync(new FeatureRequestEnvelope
            {
                Object = ConferenceProtocol.Object,
                Agent = ConferenceProtocol.Agents.Membership,
                Action = ConferenceProtocol.Actions.ListMembers,
                Ctx = new Dictionary<string, object?>
                {
                    ["conferenceId"] = ConferenceId
                }
            });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnConferenceStateChanged(object? sender, ConferenceViewState state)
        => ApplyConferenceState(state);

    private void OnSessionStateChanged(object? sender, SessionState state)
        => ApplySessionState(state);

    private void ApplyConferenceState(ConferenceViewState state)
    {
        IsBusy = state.IsBusy;
        LastError = state.LastError ?? string.Empty;
        ActiveConferenceId = string.IsNullOrWhiteSpace(state.ActiveConferenceId) ? "-" : state.ActiveConferenceId!;

        if (state.ActiveConference is not null)
        {
            OwnerPeerId = state.ActiveConference.OwnerPeerId;
            IsClosed = state.ActiveConference.IsClosed;
            Revision = state.ActiveConference.Revision;
            MemberCount = state.ActiveConference.Members.Count;
            MembersText = BuildMembersText(state.ActiveConference.Members);
        }
    }

    private void ApplySessionState(SessionState state)
    {
        TrustedPeer = string.IsNullOrWhiteSpace(state.TrustedPeer) ? "-" : state.TrustedPeer!;
        ConnectionState = state.ConnectionState.ToString();
    }

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        LastServerMessage = FormatEnvelope(envelope);

        if (!string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal))
            return;

        if (!string.Equals(envelope.Object, ConferenceProtocol.Object, StringComparison.Ordinal))
            return;

        if (envelope.Ok == false)
        {
            LastError = envelope.Message ?? "Server returned an error.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Message))
            TryApplyConferencePayload(envelope.Message);
    }

    private void TryApplyConferencePayload(string rawMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;

            if (root.TryGetProperty("conferenceId", out var conferenceIdProp))
                ActiveConferenceId = conferenceIdProp.GetString() ?? "-";

            if (root.TryGetProperty("ownerPeerId", out var ownerProp))
                OwnerPeerId = ownerProp.GetString() ?? "-";

            if (root.TryGetProperty("isClosed", out var closedProp) &&
                closedProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                IsClosed = closedProp.GetBoolean();
            }

            if (root.TryGetProperty("revision", out var revisionProp) &&
                revisionProp.TryGetUInt64(out var revision))
            {
                Revision = revision;
            }

            if (root.TryGetProperty("memberCount", out var memberCountProp) &&
                memberCountProp.TryGetInt32(out var memberCount))
            {
                MemberCount = memberCount;
            }

            if (root.TryGetProperty("members", out var membersProp) &&
                membersProp.ValueKind == JsonValueKind.Array)
            {
                var members = new List<ConferenceMember>();

                foreach (var item in membersProp.EnumerateArray())
                {
                    var peerId = item.TryGetProperty("peerId", out var peerProp)
                        ? peerProp.GetString() ?? string.Empty
                        : string.Empty;

                    var sessionId = item.TryGetProperty("sessionId", out var sessionProp)
                        ? sessionProp.GetString() ?? string.Empty
                        : string.Empty;

                    var isOwner = item.TryGetProperty("isOwner", out var ownerFlagProp) &&
                                  ownerFlagProp.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                  ownerFlagProp.GetBoolean();

                    members.Add(new ConferenceMember(peerId, sessionId, isOwner));
                }

                MemberCount = members.Count;
                MembersText = BuildMembersText(members);
            }
        }
        catch
        {
        }
    }

    private static string BuildMembersText(IReadOnlyList<ConferenceMember> members)
    {
        if (members.Count == 0)
            return "No members";

        var sb = new StringBuilder();
        foreach (var member in members)
        {
            sb.Append(member.PeerId);

            if (member.IsOwner)
                sb.Append(" (owner)");

            if (!string.IsNullOrWhiteSpace(member.SessionId))
            {
                sb.Append(" | session: ");
                sb.Append(member.SessionId);
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatEnvelope(FeatureResponseEnvelope envelope)
    {
        var sb = new StringBuilder();

        sb.Append("type=").Append(envelope.Type);

        if (!string.IsNullOrWhiteSpace(envelope.Object))
            sb.Append(", object=").Append(envelope.Object);

        if (!string.IsNullOrWhiteSpace(envelope.Agent))
            sb.Append(", agent=").Append(envelope.Agent);

        if (!string.IsNullOrWhiteSpace(envelope.Action))
            sb.Append(", action=").Append(envelope.Action);

        if (!string.IsNullOrWhiteSpace(envelope.Peer))
            sb.Append(", peer=").Append(envelope.Peer);

        if (envelope.Ok.HasValue)
            sb.Append(", ok=").Append(envelope.Ok.Value);

        if (!string.IsNullOrWhiteSpace(envelope.Message))
            sb.Append(", message=").Append(envelope.Message);

        return sb.ToString();
    }

    public void Dispose()
    {
        _conferenceStore.StateChanged -= OnConferenceStateChanged;
        _sessionStore.StateChanged -= OnSessionStateChanged;
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}