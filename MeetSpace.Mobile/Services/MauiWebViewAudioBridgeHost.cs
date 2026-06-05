using MeetSpace.Client.App.Calls;
using Microsoft.Maui.ApplicationModel;
using System.Text;

namespace MeetSpace.Mobile.Services;

public sealed class MauiWebViewAudioBridgeHost : IAudioBridgeHost
{
	private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(15);
	private const string BridgePostPrefix = "meetspace-bridge://post?data=";

	private readonly WebView _webView;
	private bool _initialized;
	private bool _disposed;
	private TaskCompletionSource<bool>? _navigationTcs;

	public event EventHandler<string>? MessageReceived;
	public bool IsDisposed => _disposed;

	public MauiWebViewAudioBridgeHost(WebView webView)
	{
		_webView = webView ?? throw new ArgumentNullException(nameof(webView));
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		if (_initialized)
			return;

		var indexHtml = await LoadAppPackageTextAsync("MediaHost/index.html", cancellationToken);
		var bundleJs = await LoadAppPackageTextAsync("MediaHost/bridge.bundle.js", cancellationToken);
		var html = BuildHostDocument(indexHtml, bundleJs);

		var navigationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_navigationTcs = navigationTcs;

		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			_webView.Navigating -= WebView_Navigating;
			_webView.Navigated -= WebView_Navigated;
			_webView.Navigating += WebView_Navigating;
			_webView.Navigated += WebView_Navigated;
			_webView.Source = new HtmlWebViewSource
			{
				Html = html
			};
		});

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(NavigationTimeout);
		using var registration = timeoutCts.Token.Register(() =>
		{
			navigationTcs.TrySetException(new TimeoutException("Media host navigation timed out."));
		});

		await navigationTcs.Task.ConfigureAwait(false);
		_initialized = true;
	}

	public async Task PostJsonAsync(string json, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		if (!_initialized)
			throw new InvalidOperationException("Audio bridge host is not initialized.");
		if (string.IsNullOrWhiteSpace(json))
			throw new ArgumentException("json is empty.", nameof(json));

		cancellationToken.ThrowIfCancellationRequested();

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			var jsonLiteral = System.Text.Json.JsonSerializer.Serialize(json);
			var script = "(function(){if(window.__meetspaceNativeDeliver){window.__meetspaceNativeDeliver(" +
				jsonLiteral +
				");return 'ok';}throw new Error('meetspace bridge receiver missing');})();";

			await _webView.EvaluateJavaScriptAsync(script);
		});
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		try
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				_webView.Navigating -= WebView_Navigating;
				_webView.Navigated -= WebView_Navigated;
			});
		}
		catch
		{
		}
	}

	private void WebView_Navigated(object? sender, WebNavigatedEventArgs e)
	{
		var tcs = _navigationTcs;
		_navigationTcs = null;

		if (tcs == null)
			return;

		if (e.Result == WebNavigationResult.Success)
		{
			tcs.TrySetResult(true);
			return;
		}

		tcs.TrySetException(new InvalidOperationException("Media host navigation failed: " + e.Result));
	}

	private void WebView_Navigating(object? sender, WebNavigatingEventArgs e)
	{
		var url = e.Url;
		if (string.IsNullOrWhiteSpace(url))
			return;

		if (!url.StartsWith(BridgePostPrefix, StringComparison.OrdinalIgnoreCase))
			return;

		e.Cancel = true;
		var payload = TryDecodeBridgePayload(url);
		if (payload == null)
			return;

		MessageReceived?.Invoke(this, payload);
	}

	private static string BuildHostDocument(string indexHtml, string bridgeBundle)
	{
		var bridgeShim = BuildBridgeShimScript();
		var embeddedBundle = "<script>\n" + EscapeScript(bridgeBundle) + "\n</script>";
		var replacement = bridgeShim + "\n" + embeddedBundle;

		var replaced = indexHtml.Replace(
			"<script src=\"bridge.bundle.js\"></script>",
			replacement,
			StringComparison.OrdinalIgnoreCase);

		if (!ReferenceEquals(replaced, indexHtml) || replaced.Length != indexHtml.Length)
			return replaced;

		var bodyCloseTag = "</body>";
		var bodyIndex = indexHtml.LastIndexOf(bodyCloseTag, StringComparison.OrdinalIgnoreCase);
		if (bodyIndex < 0)
			return indexHtml + "\n" + replacement;

		return indexHtml.Insert(bodyIndex, "\n" + replacement + "\n");
	}

	private static string EscapeScript(string scriptContent)
	{
		if (string.IsNullOrEmpty(scriptContent))
			return string.Empty;

		return scriptContent
			.Replace("</script>", "<\\/script>")
			.Replace("</SCRIPT>", "<\\/script>");
	}

	private static string BuildBridgeShimScript()
	{
		var sb = new StringBuilder();
		sb.AppendLine("<script>");
		sb.AppendLine("(function(){");
		sb.AppendLine("  const listeners = [];");
		sb.AppendLine("  function normalize(raw){");
		sb.AppendLine("    if(typeof raw === 'string') return raw;");
		sb.AppendLine("    try { return JSON.stringify(raw); } catch(_) { return String(raw); }");
		sb.AppendLine("  }");
		sb.AppendLine("  function postMessage(raw){");
		sb.AppendLine("    const payload = encodeURIComponent(normalize(raw));");
		sb.AppendLine("    window.location.href = 'meetspace-bridge://post?data=' + payload;");
		sb.AppendLine("  }");
		sb.AppendLine("  function addEventListener(name, handler){");
		sb.AppendLine("    if(name !== 'message' || typeof handler !== 'function') return;");
		sb.AppendLine("    listeners.push(handler);");
		sb.AppendLine("  }");
		sb.AppendLine("  function removeEventListener(name, handler){");
		sb.AppendLine("    if(name !== 'message') return;");
		sb.AppendLine("    const index = listeners.indexOf(handler);");
		sb.AppendLine("    if(index >= 0) listeners.splice(index, 1);");
		sb.AppendLine("  }");
		sb.AppendLine("  function deliver(raw){");
		sb.AppendLine("    const event = { data: normalize(raw) };");
		sb.AppendLine("    const snapshot = listeners.slice();");
		sb.AppendLine("    for(const handler of snapshot){");
		sb.AppendLine("      try { handler(event); } catch(_) {}");
		sb.AppendLine("    }");
		sb.AppendLine("  }");
		sb.AppendLine("  window.chrome = window.chrome || {};");
		sb.AppendLine("  window.chrome.webview = window.chrome.webview || {};");
		sb.AppendLine("  window.chrome.webview.postMessage = postMessage;");
		sb.AppendLine("  window.chrome.webview.addEventListener = addEventListener;");
		sb.AppendLine("  window.chrome.webview.removeEventListener = removeEventListener;");
		sb.AppendLine("  window.__meetspaceNativeDeliver = deliver;");
		sb.AppendLine("})();");
		sb.AppendLine("</script>");
		return sb.ToString();
	}

	private static async Task<string> LoadAppPackageTextAsync(string path, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var stream = await FileSystem.Current.OpenAppPackageFileAsync(path);
		using var reader = new StreamReader(stream);
		var text = await reader.ReadToEndAsync();
		cancellationToken.ThrowIfCancellationRequested();
		return text;
	}

	private static string? TryDecodeBridgePayload(string url)
	{
		try
		{
			var index = url.IndexOf("data=", StringComparison.OrdinalIgnoreCase);
			if (index < 0)
				return null;

			var encoded = url[(index + 5)..];
			return Uri.UnescapeDataString(encoded);
		}
		catch
		{
			return null;
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
			throw new ObjectDisposedException(nameof(MauiWebViewAudioBridgeHost));
	}
}
