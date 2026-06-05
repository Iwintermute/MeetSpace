namespace MeetSpace.Mobile.Controls;

public partial class DirectCallIncomingOverlay : ContentView
{
	public static readonly BindableProperty IncomingCallTextProperty =
		BindableProperty.Create(
			nameof(IncomingCallText),
			typeof(string),
			typeof(DirectCallIncomingOverlay),
			string.Empty);

	public DirectCallIncomingOverlay()
	{
		InitializeComponent();
	}

	public string IncomingCallText
	{
		get => (string)GetValue(IncomingCallTextProperty);
		set => SetValue(IncomingCallTextProperty, value);
	}

	public event EventHandler? AcceptAudioRequested;
	public event EventHandler? AcceptVideoRequested;
	public event EventHandler? DeclineRequested;

	private void AcceptAudioButton_Clicked(object? sender, EventArgs e)
	{
		AcceptAudioRequested?.Invoke(this, EventArgs.Empty);
	}

	private void AcceptVideoButton_Clicked(object? sender, EventArgs e)
	{
		AcceptVideoRequested?.Invoke(this, EventArgs.Empty);
	}

	private void DeclineButton_Clicked(object? sender, EventArgs e)
	{
		DeclineRequested?.Invoke(this, EventArgs.Empty);
	}
}
