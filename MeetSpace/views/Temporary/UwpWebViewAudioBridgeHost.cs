using MeetSpace.Client.App.Calls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.UI.Core;

namespace MeetSpace.Views.Temporary
{
    public sealed class UwpWebViewAudioBridgeHost : IAudioBridgeHost
    {
        private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(10);
        private static readonly string HostName = "appassets.meetspace";
        private static readonly Uri MediaHostUri = new Uri("https://" + HostName + "/index.html");
        private static int _runtimeFlagsConfigured;

        private readonly WebView2 _webView;
        private bool _initialized;
        private bool _disposed;
        private TaskCompletionSource<bool>? _navigationTcs;
        private EventInfo? _screenCaptureStartingEvent;
        private Delegate? _screenCaptureStartingHandler;
        private MethodInfo? _screenCaptureStartingCallbackMethod;

        public event EventHandler<string>? MessageReceived;
        public bool IsDisposed => _disposed;

        public UwpWebViewAudioBridgeHost(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_initialized)
                return;
            EnsureWebViewRuntimeFlagsConfigured();

            await RunOnUiAsync(async () =>
            {
                try
                {
                    await _webView.EnsureCoreWebView2Async().AsTask().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "WebView2 initialization failed. " + ex.Message,
                        ex);
                }

                if (_webView.CoreWebView2 == null)
                    throw new InvalidOperationException("CoreWebView2 is null after EnsureCoreWebView2Async.");

                _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                var mediaHostFolder = Path.Combine(
                    Package.Current.InstalledLocation.Path,
                    "WebView",
                    "MediaHost");

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    HostName,
                    mediaHostFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                _webView.CoreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;
                _webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;

                _webView.CoreWebView2.PermissionRequested -= CoreWebView2_PermissionRequested;
                _webView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                TryAttachScreenCaptureStartingHandler();

                _webView.NavigationCompleted -= WebView_NavigationCompleted;
                _webView.NavigationCompleted += WebView_NavigationCompleted;
            }).ConfigureAwait(false);

            var navigationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _navigationTcs = navigationTcs;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(NavigationTimeout);

            using var registration = linkedCts.Token.Register(() =>
            {
                navigationTcs.TrySetException(new TimeoutException("Media host navigation timed out."));
            });

            await RunOnUiAsync(() =>
            {
                if (_webView.CoreWebView2 == null)
                    throw new InvalidOperationException("CoreWebView2 is null.");

                _webView.CoreWebView2.Navigate(MediaHostUri.ToString());
            }).ConfigureAwait(false);

            await navigationTcs.Task.ConfigureAwait(false);
            await RunOnUiAsync(async () =>
            {
                if (_webView.CoreWebView2 == null)
                    return;

                try
                {
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        "try{if(window.__meetspaceForceHostReady){window.__meetspaceForceHostReady();}true;}catch(_){false;}")
                        .AsTask()
                        .ConfigureAwait(true);
                }
                catch
                {
                }
            }).ConfigureAwait(false);
            _initialized = true;
        }

        public Task PostJsonAsync(string json, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("json is empty.", nameof(json));

            if (!_initialized)
                throw new InvalidOperationException("Audio bridge host is not initialized.");

            return RunOnUiAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_webView.CoreWebView2 == null)
                    throw new InvalidOperationException("CoreWebView2 is not initialized.");

                _webView.CoreWebView2.PostWebMessageAsString(json);
            });
        }

        private void CoreWebView2_PermissionRequested(
            CoreWebView2 sender,
            CoreWebView2PermissionRequestedEventArgs args)
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.Microphone ||
                args.PermissionKind == CoreWebView2PermissionKind.Camera)
            {
                args.State = CoreWebView2PermissionState.Allow;
                args.Handled = true;
                return;
            }

            args.Handled = false;
        }
        private void TryAttachScreenCaptureStartingHandler()
        {
            if (_webView.CoreWebView2 == null || _screenCaptureStartingHandler != null)
                return;

            try
            {
                var eventInfo = _webView.CoreWebView2.GetType().GetEvent("ScreenCaptureStarting");
                if (eventInfo == null || eventInfo.EventHandlerType == null)
                    return;
                var callbackMethod = _screenCaptureStartingCallbackMethod
                    ??= GetType().GetMethod(
                        nameof(CoreWebView2_ScreenCaptureStarting),
                        BindingFlags.Instance | BindingFlags.NonPublic);
                if (callbackMethod == null)
                    return;

                var handler = Delegate.CreateDelegate(
                    eventInfo.EventHandlerType,
                    this,
                    callbackMethod,
                    false);
                if (handler == null)
                    return;

                eventInfo.AddEventHandler(_webView.CoreWebView2, handler);
                _screenCaptureStartingEvent = eventInfo;
                _screenCaptureStartingHandler = handler;
            }
            catch
            {
            }
        }

        private void CoreWebView2_ScreenCaptureStarting(object sender, object args)
        {
            if (args == null)
                return;

            try
            {
                var argsType = args.GetType();

                var cancelProperty = argsType.GetProperty("Cancel");
                if (cancelProperty?.CanWrite == true && cancelProperty.PropertyType == typeof(bool))
                    cancelProperty.SetValue(args, false);

                var handledProperty = argsType.GetProperty("Handled");
                if (handledProperty?.CanWrite == true && handledProperty.PropertyType == typeof(bool))
                    handledProperty.SetValue(args, false);
            }
            catch
            {
            }
        }
        private void DetachScreenCaptureStartingHandler()
        {
            if (_webView.CoreWebView2 == null ||
                _screenCaptureStartingEvent == null ||
                _screenCaptureStartingHandler == null)
            {
                return;
            }

            try
            {
                _screenCaptureStartingEvent.RemoveEventHandler(
                    _webView.CoreWebView2,
                    _screenCaptureStartingHandler);
            }
            catch
            {
            }
            finally
            {
                _screenCaptureStartingEvent = null;
                _screenCaptureStartingHandler = null;
            }
        }

        private static void EnsureWebViewRuntimeFlagsConfigured()
        {
            if (Interlocked.Exchange(ref _runtimeFlagsConfigured, 1) != 0)
                return;

            const string additionalArgsName = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
            const string compatibilityArg = "--disable-features=AllowWgcScreenCapturer";

            try
            {
                var existing = Environment.GetEnvironmentVariable(additionalArgsName);
                if (string.IsNullOrWhiteSpace(existing))
                {
                    Environment.SetEnvironmentVariable(additionalArgsName, compatibilityArg);
                    return;
                }

                if (existing.IndexOf("AllowWgcScreenCapturer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

                Environment.SetEnvironmentVariable(
                    additionalArgsName,
                    (existing + " " + compatibilityArg).Trim());
            }
            catch
            {
            }
        }


        private void WebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            var tcs = _navigationTcs;
            _navigationTcs = null;

            if (tcs == null)
                return;

            if (args.IsSuccess)
            {
                tcs.TrySetResult(true);
                return;
            }

            tcs.TrySetException(
                new InvalidOperationException("Media host navigation failed: " + args.WebErrorStatus));
        }

        private void CoreWebView2_ProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
        {
            var tcs = _navigationTcs;
            if (tcs != null && !tcs.Task.IsCompleted)
            {
                tcs.TrySetException(new InvalidOperationException(
                    "WebView2 process failed: " + args.ProcessFailedKind));
            }
        }

        private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string raw;

            try
            {
                raw = args.TryGetWebMessageAsString();
            }
            catch
            {
                try
                {
                    raw = args.WebMessageAsJson;
                }
                catch
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(raw))
                return;

            MessageReceived?.Invoke(this, raw);
        }

        private Task RunOnUiAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = _webView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private Task RunOnUiAsync(Func<Task> action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = _webView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UwpWebViewAudioBridgeHost));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _webView.NavigationCompleted -= WebView_NavigationCompleted;
            }
            catch
            {
            }

            try
            {
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    _webView.CoreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;
                    _webView.CoreWebView2.PermissionRequested -= CoreWebView2_PermissionRequested;
                }
            }
            catch
            {
            }

            DetachScreenCaptureStartingHandler();
        }
    }
}