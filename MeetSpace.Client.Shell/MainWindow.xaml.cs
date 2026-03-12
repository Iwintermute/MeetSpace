using System.Windows;
using MeetSpace.Client.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeetSpace.Client.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var shellViewModel = App.HostContainer.Services.GetRequiredService<ShellViewModel>();
        DataContext = shellViewModel;
    }
}