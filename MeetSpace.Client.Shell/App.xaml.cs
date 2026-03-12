using System.Windows;
using MeetSpace.Client.Bootstrap;
using Microsoft.Extensions.Hosting;

namespace MeetSpace.Client.Shell;

public partial class App : Application
{
    public static IHost HostContainer { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostContainer = MeetSpaceHostBuilder.Build();
        await HostContainer.StartAsync();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (HostContainer is not null)
        {
            await HostContainer.StopAsync();
            HostContainer.Dispose();
        }

        base.OnExit(e);
    }
}