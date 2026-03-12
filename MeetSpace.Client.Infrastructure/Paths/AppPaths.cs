using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Infrastructure.Paths;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeetSpace");

        LogsDirectory = Path.Combine(AppDataDirectory, "logs");
        SettingsDirectory = Path.Combine(AppDataDirectory, "settings");

        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }

    public string AppDataDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsDirectory { get; }
}