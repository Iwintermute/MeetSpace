using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MeetSpace.Mobile.ViewModels;

public sealed class DirectCallNavigationRequest
{
	public DirectCallNavigationRequest(
		string callId,
		string? counterpartUserId,
		string counterpartTitle,
		bool isIncoming,
		bool autoEnableCamera)
	{
		CallId = callId;
		CounterpartUserId = counterpartUserId;
		CounterpartTitle = counterpartTitle;
		IsIncoming = isIncoming;
		AutoEnableCamera = autoEnableCamera;
	}

	public string CallId { get; }
	public string? CounterpartUserId { get; }
	public string CounterpartTitle { get; }
	public bool IsIncoming { get; }
	public bool AutoEnableCamera { get; }
}

public sealed class DirectCallParticipantViewItem
{
	public DirectCallParticipantViewItem(
		string title,
		bool hasAudio,
		bool hasVideo,
		bool hasScreenShare)
	{
		Title = title;
		HasAudio = hasAudio;
		HasVideo = hasVideo;
		HasScreenShare = hasScreenShare;
	}

	public string Title { get; }
	public bool HasAudio { get; }
	public bool HasVideo { get; }
	public bool HasScreenShare { get; }
	public string MediaSummary =>
		(HasAudio ? "🎤 " : string.Empty) +
		(HasVideo ? "📷 " : string.Empty) +
		(HasScreenShare ? "🖥 " : string.Empty);
}

public sealed class DirectChatMessageViewItem
{
	private static readonly Color OwnBubbleColorFallback = Color.FromArgb("#E3E3E3");
	private static readonly Color RemoteBubbleColorFallback = Color.FromArgb("#EEEEEE");

	public DirectChatMessageViewItem(
		string senderTitle,
		string senderMeta,
		string text,
		string timeText,
		string deliveryStatusText,
		bool isOwn)
	{
		SenderTitle = senderTitle;
		SenderMeta = senderMeta;
		Text = text;
		TimeText = timeText;
		DeliveryStatusText = deliveryStatusText;
		IsOwn = isOwn;
	}

	public string SenderTitle { get; }
	public string SenderMeta { get; }
	public string Text { get; }
	public string TimeText { get; }
	public string DeliveryStatusText { get; }
	public bool IsOwn { get; }
	public LayoutOptions BubbleAlignment => IsOwn ? LayoutOptions.End : LayoutOptions.Start;
	public Color BubbleBackgroundColor => IsOwn
		? ResolveColor("1cBrush", OwnBubbleColorFallback)
		: ResolveColor("11Brush", RemoteBubbleColorFallback);
	public string FooterText => string.IsNullOrWhiteSpace(DeliveryStatusText)
		? TimeText
		: TimeText + " • " + DeliveryStatusText;

	private static Color ResolveColor(string key, Color fallback)
	{
		if (Application.Current?.Resources == null)
			return fallback;

		if (!Application.Current.Resources.TryGetValue(key, out var value))
			return fallback;

		if (value is Color color)
			return color;

		if (value is SolidColorBrush brush)
			return brush.Color;

		return fallback;
	}
}

public sealed class ConferenceChatMessageViewItem
{
	public ConferenceChatMessageViewItem(
		string senderDisplayName,
		string text,
		string displayTime,
		bool isOwn)
	{
		SenderDisplayName = senderDisplayName;
		Text = text;
		DisplayTime = displayTime;
		IsOwn = isOwn;
	}

	public string SenderDisplayName { get; }
	public string Text { get; }
	public string DisplayTime { get; }
	public bool IsOwn { get; }
}
