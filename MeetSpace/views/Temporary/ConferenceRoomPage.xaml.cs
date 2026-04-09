using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Bootstrap;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Domain.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MeetSpace.Views.Temporary
{
    public sealed partial class ConferenceRoomPage : Page
    {
        private readonly ChatCoordinator _chatCoordinator;
        private readonly ChatStore _chatStore;
        private readonly ConferenceCoordinator _conferenceCoordinator;
        private readonly CallCoordinator _callCoordinator;
        private readonly CallStore _callStore;
        private readonly AuthSessionStore _authStore;
        private readonly SessionStore _sessionStore;
        private readonly RealtimeStartupService _realtimeStartupService;
        private readonly RealtimeAuthBinder _realtimeAuthBinder;

        private string _conferenceId = string.Empty;
        private CancellationTokenSource? _pageLifetimeCts;
        private UwpWebViewAudioBridgeHost? _audioBridgeHost;
        private WebView2? _mediaHostView;
        private bool _audioBridgeReady;

        public ObservableCollection<ConferenceChatMessageViewItem> Messages { get; } =
            new ObservableCollection<ConferenceChatMessageViewItem>();

        public ConferenceRoomPage()
        {
            this.InitializeComponent();

            var services = App.Current.Services;
            _chatCoordinator = services.GetRequiredService<ChatCoordinator>();
            _chatStore = services.GetRequiredService<ChatStore>();
            _conferenceCoordinator = services.GetRequiredService<ConferenceCoordinator>();
            _callCoordinator = services.GetRequiredService<CallCoordinator>();
            _callStore = services.GetRequiredService<CallStore>();

            _authStore = services.GetRequiredService<AuthSessionStore>();
            _sessionStore = services.GetRequiredService<SessionStore>();
            _realtimeStartupService = services.GetRequiredService<RealtimeStartupService>();
            _realtimeAuthBinder = services.GetRequiredService<RealtimeAuthBinder>();

            _audioBridgeReady = false;
            MicrophoneButton.IsEnabled = false;

            Loaded += ConferenceRoomPage_Loaded;
            Unloaded += ConferenceRoomPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _conferenceId = e.Parameter as string ?? string.Empty;
            _audioBridgeReady = false;
        }

        private async void ConferenceRoomPage_Loaded(object sender, RoutedEventArgs e)
        {
            _chatStore.StateChanged += ChatStore_StateChanged;
            _authStore.StateChanged += AuthStore_StateChanged;
            _sessionStore.StateChanged += SessionStore_StateChanged;
            _callStore.StateChanged += CallStore_StateChanged;

            _pageLifetimeCts?.Cancel();
            _pageLifetimeCts?.Dispose();
            _pageLifetimeCts = new CancellationTokenSource();

            _audioBridgeReady = false;
            ApplyCallState(_callStore.Current);

            var pageToken = _pageLifetimeCts.Token;

            try
            {
                var ok = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!ok || pageToken.IsCancellationRequested)
                    return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    EnsureMediaHostViewCreated();
                });

                if (_mediaHostView == null)
                    throw new InvalidOperationException("MediaHostView was not created.");


                if (pageToken.IsCancellationRequested)
                    return;

              

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ApplyIdentity(_authStore.Current, _sessionStore.Current);
                    ApplyChatState(_chatStore.Current);
                    ApplyCallState(_callStore.Current);
                });

                var peerReady = await WaitForTrustedPeerAsync(TimeSpan.FromSeconds(10), pageToken).ConfigureAwait(false);
                if (!peerReady || pageToken.IsCancellationRequested)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        AuthorizedUserMetaTextBlock.Text = "peer не назначен";
                    });

                    return;
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ApplyIdentity(_authStore.Current, _sessionStore.Current);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _audioBridgeReady = false;

                try
                {
                    _audioBridgeHost?.Dispose();
                }
                catch
                {
                }

                _audioBridgeHost = null;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ApplyCallState(_callStore.Current);
                    AuthorizedUserMetaTextBlock.Text = "media host init failed: " + ex.Message;
                });
            }
        }

        private async void ConferenceRoomPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _chatStore.StateChanged -= ChatStore_StateChanged;
            _authStore.StateChanged -= AuthStore_StateChanged;
            _sessionStore.StateChanged -= SessionStore_StateChanged;
            _callStore.StateChanged -= CallStore_StateChanged;

            _audioBridgeReady = false;

            var lifetime = _pageLifetimeCts;
            _pageLifetimeCts = null;

            try
            {
                lifetime?.Cancel();
            }
            catch
            {
            }

            try
            {
                await _callCoordinator.LeaveAudioAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                lifetime?.Dispose();
            }
            catch
            {
            }

            try
            {
                _audioBridgeHost?.Dispose();
            }
            catch
            {
            }

            _audioBridgeHost = null;

            try
            {
                if (_mediaHostView != null)
                {
                    MediaHostContainer.Children.Clear();
                    _mediaHostView = null;
                }
            }
            catch
            {
            }
        }

        private void EnsureMediaHostViewCreated()
        {
            if (_mediaHostView != null)
                return;

            var webView = new WebView2
            {
                Width = 1,
                Height = 1,
                Opacity = 0.01,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            MediaHostContainer.Children.Clear();
            MediaHostContainer.Children.Add(webView);
            _mediaHostView = webView;
        }

        private async void AuthStore_StateChanged(object sender, AuthSessionState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!state.IsAuthenticated)
                {
                    Frame?.Navigate(typeof(LoginPage));
                    return;
                }

                ApplyIdentity(state, _sessionStore.Current);
            });
        }

        private async void SessionStore_StateChanged(object sender, SessionState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyIdentity(_authStore.Current, state);
            });
        }

        private async void CallStore_StateChanged(object sender, CallSessionState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyCallState(state);
            });
        }

        private async Task<bool> EnsureAuthorizedAsync()
        {
            var auth = _authStore.Current;

            if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.UserId))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame?.Navigate(typeof(LoginPage));
                });

                return false;
            }

            try
            {
                var result = await _realtimeStartupService.EnsureConnectedAsync("ws://127.0.0.1:9002").ConfigureAwait(false);
                if (result.IsSuccess)
                    await _realtimeAuthBinder.BindAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            return true;
        }

        private void ApplyIdentity(AuthSessionState auth, SessionState session)
        {
            var displayName = !string.IsNullOrWhiteSpace(auth.Email)
                ? auth.Email
                : auth.UserId ?? "Авторизованный пользователь";

            AuthorizedUserTextBlock.Text = displayName;

            var peer = !string.IsNullOrWhiteSpace(session.TrustedPeer)
                ? session.TrustedPeer
                : "peer не назначен";

            var stage = _callStore.Current.Stage.ToString();

            AuthorizedUserMetaTextBlock.Text = string.IsNullOrWhiteSpace(_conferenceId)
                ? peer + " • " + stage
                : peer + " • " + _conferenceId + " • " + stage;
        }

        private void ApplyCallState(CallSessionState state)
        {
            var joinInProgress =
                state.Stage == CallConnectionStage.JoiningRoom ||
                state.Stage == CallConnectionStage.TransportOpening ||
                state.Stage == CallConnectionStage.Negotiating ||
                state.Stage == CallConnectionStage.Publishing;

            if (joinInProgress)
            {
                MicrophoneButton.IsEnabled = false;
                MicrophoneButton.Content = "Подключение...";
            }
            else if (state.Stage == CallConnectionStage.Connected)
            {
                MicrophoneButton.IsEnabled = true;
                MicrophoneButton.Content = state.LocalMedia.MicrophoneEnabled
                    ? "Микрофон вкл"
                    : "Микрофон выкл";
            }
            else
            {
                MicrophoneButton.IsEnabled = true;
                MicrophoneButton.Content = "Подключить аудио";
            }

            ApplyIdentity(_authStore.Current, _sessionStore.Current);
        }
        private async void ChatStore_StateChanged(object sender, ChatViewState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyChatState(state);
            });
        }

        private string ResolveSenderDisplayName(ChatMessageItem item)
        {
            if (item.IsOwn)
            {
                if (!string.IsNullOrWhiteSpace(_authStore.Current.Email))
                    return _authStore.Current.Email;

                if (!string.IsNullOrWhiteSpace(_authStore.Current.UserId))
                    return _authStore.Current.UserId;

                return "Вы";
            }

            return string.IsNullOrWhiteSpace(item.SenderPeerId)
                ? "Участник"
                : item.SenderPeerId;
        }

        private void ApplyChatState(ChatViewState state)
        {
            Messages.Clear();

            foreach (var item in state.Messages
                .Where(x =>
                    x.ConferenceId == _conferenceId &&
                    string.IsNullOrWhiteSpace(x.TargetPeerId))
                .OrderBy(x => x.SentAtUtc))
            {
                Messages.Add(new ConferenceChatMessageViewItem(
                    ResolveSenderDisplayName(item),
                    item.Text,
                    item.DisplayTime,
                    item.IsOwn));
            }

            if (Messages.Count > 0)
                MessagesList.ScrollIntoView(Messages[Messages.Count - 1]);
        }

        private void OpenChatButton_Click(object sender, RoutedEventArgs e)
        {
            ChatPanel.Visibility = Visibility.Visible;
            ChatPanelColumn.Width = new GridLength(360);
        }

        private void CloseChatButton_Click(object sender, RoutedEventArgs e)
        {
            ChatPanel.Visibility = Visibility.Collapsed;
            ChatPanelColumn.Width = new GridLength(0);
        }

        private async Task<bool> WaitForTrustedPeerAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var startedAt = DateTimeOffset.UtcNow;

            while (string.IsNullOrWhiteSpace(_sessionStore.Current.TrustedPeer))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DateTimeOffset.UtcNow - startedAt >= timeout)
                    return false;

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            var text = MessageTextBox.Text != null ? MessageTextBox.Text.Trim() : null;
            if (string.IsNullOrWhiteSpace(text))
                return;

            var result = await _chatCoordinator.SendMessageAsync(_conferenceId, text).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    MessageTextBox.Text = string.Empty;
                });
            }
        }

        private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_conferenceId))
                return;

            var token = _pageLifetimeCts != null ? _pageLifetimeCts.Token : CancellationToken.None;
            var stage = _callStore.Current.Stage;

            try
            {
                if (!_audioBridgeReady)
                    await EnsureAudioBridgeReadyAsync(token).ConfigureAwait(false);

                stage = _callStore.Current.Stage;

                if (stage == CallConnectionStage.Idle || stage == CallConnectionStage.Faulted)
                {
                    var result = await _callCoordinator.JoinAudioAsync(_conferenceId, token).ConfigureAwait(false);
                    if (result.IsFailure && !token.IsCancellationRequested)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            AuthorizedUserMetaTextBlock.Text = result.Error?.Message ?? "audio start failed";
                        });
                    }

                    return;
                }

                if (stage == CallConnectionStage.JoiningRoom ||
                    stage == CallConnectionStage.TransportOpening ||
                    stage == CallConnectionStage.Negotiating ||
                    stage == CallConnectionStage.Publishing)
                {
                    return;
                }

                if (stage != CallConnectionStage.Connected)
                    return;

                var toggleResult = await _callCoordinator.ToggleMicrophoneAsync(token).ConfigureAwait(false);
                if (toggleResult.IsFailure && !token.IsCancellationRequested)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        AuthorizedUserMetaTextBlock.Text = toggleResult.Error?.Message ?? "mic toggle failed";
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    AuthorizedUserMetaTextBlock.Text = "audio bridge init failed: " + ex.Message;
                });
            }
        }
        private async Task EnsureAudioBridgeReadyAsync(CancellationToken cancellationToken)
        {
            if (_audioBridgeReady)
                return;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                EnsureMediaHostViewCreated();
            });

            if (_mediaHostView == null)
                throw new InvalidOperationException("MediaHostView was not created.");

            try
            {
                _audioBridgeHost?.Dispose();
            }
            catch
            {
            }

            _audioBridgeHost = new UwpWebViewAudioBridgeHost(_mediaHostView);
            await _callCoordinator.AttachHostAsync(_audioBridgeHost, cancellationToken).ConfigureAwait(false);

            _audioBridgeReady = true;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyCallState(_callStore.Current);
            });
        }
        private async void LeaveConferenceButton_Click(object sender, RoutedEventArgs e)
        {
            var conferenceId = _conferenceId;

            try
            {
                _pageLifetimeCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                await _callCoordinator.LeaveAudioAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            _chatStore.ResetConference(conferenceId);

            if (!string.IsNullOrWhiteSpace(conferenceId))
                _ = _conferenceCoordinator.LeaveConferenceAsync(conferenceId);

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (Frame.CanGoBack)
                    Frame.GoBack();
            });
        }
    }
}