using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MeetSpace.Views.UserControls
{
    public sealed partial class MeetingInviteOverlay : UserControl
    {
        private string _conferenceId;
        private string _link;

        public event EventHandler<string> JoinRequested;
        public event EventHandler Closed;

        public MeetingInviteOverlay()
        {
            this.InitializeComponent();
        }

        public void Show(string link, string conferenceId)
        {
            _link = link;
            _conferenceId = conferenceId;
            LinkTextBlock.Text = link;
            Visibility = Visibility.Visible;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText(_link);
            Clipboard.SetContent(package);
        }

        private void OpenMeetingButton_Click(object sender, RoutedEventArgs e)
        {
            JoinRequested?.Invoke(this, _conferenceId);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}