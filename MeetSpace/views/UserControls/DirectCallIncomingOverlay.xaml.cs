using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MeetSpace.Views.UserControls;

public sealed partial class DirectCallIncomingOverlay : UserControl
{
    public static readonly DependencyProperty IncomingCallTextProperty =
        DependencyProperty.Register(
            nameof(IncomingCallText),
            typeof(string),
            typeof(DirectCallIncomingOverlay),
            new PropertyMetadata(string.Empty));

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

    private void AcceptAudioButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptAudioRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AcceptVideoButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptVideoRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        DeclineRequested?.Invoke(this, EventArgs.Empty);
    }
}
