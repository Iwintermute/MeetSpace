using Microsoft.Maui.ApplicationModel;

namespace MeetSpace.Mobile.Controls;

public partial class MeetingInviteOverlay : ContentView
{
	private string _conferenceId = string.Empty;
	private string _link = string.Empty;

	public event EventHandler<string>? JoinRequested;
	public event EventHandler? Closed;

	public MeetingInviteOverlay()
	{
		InitializeComponent();
	}

	public void Show(string link, string conferenceId)
	{
		_link = link ?? string.Empty;
		_conferenceId = conferenceId ?? string.Empty;
		LinkLabel.Text = _link;
		IsVisible = true;
	}

	private async void CopyButton_Clicked(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_link))
			return;

		await Clipboard.Default.SetTextAsync(_link);
	}

	private void OpenMeetingButton_Clicked(object? sender, EventArgs e)
	{
		JoinRequested?.Invoke(this, _conferenceId);
	}

	private void CloseButton_Clicked(object? sender, EventArgs e)
	{
		IsVisible = false;
		Closed?.Invoke(this, EventArgs.Empty);
	}
}
